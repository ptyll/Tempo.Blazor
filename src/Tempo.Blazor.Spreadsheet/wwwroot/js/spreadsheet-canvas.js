window.tmSpreadsheetCanvas = window.tmSpreadsheetCanvas || {};

(function () {
    const stateKey = "__tmSpreadsheetCanvas";
    const imageCache = new Map();
    const modelRootMap = new WeakMap();
    const maxTextMeasureCacheSize = 5000;
    const maxStyleCacheSize = 1200;
    const maxDisplayValueCacheSize = 10000;
    const defaultCellFontFamily = "ui-monospace, SFMono-Regular, Menlo, Consolas, monospace";
    // Header labels are UI text, not cell data: the sans stack comes from --tm-spreadsheet-header-font
    // (the token wins); this is only the fallback when a host loads the canvas without the stylesheet.
    const defaultHeaderFont = "500 11px system-ui, -apple-system, Segoe UI, sans-serif";
    // SP1 density render scales. Comfortable is a RENDER scale only — row heights are scaled by the
    // component's geometry, fonts/handles here; the workbook model and every XLSX height stay exact.
    const comfortableRowScale = 1.5;
    const comfortableFontScale = 1.25;
    const comfortableHandleScale = 1.4;
    // Header gutters (row-header width 40 -> 56, column-header height 20 -> 28). This value MIRRORS
    // SpreadsheetDensityScale.ComfortableGutterScale for the node lane and for a frame that does not
    // carry the scales; the canvas itself never multiplies a gutter — the component ships the scaled
    // RowHeaderWidth/ColumnHeaderHeight in the frame (one source of truth, see resolveContentPoint).
    const comfortableGutterScale = 1.4;
    const selectionFillAlphaDefault = 0.12;
    const keyboardRepeatPauseMs = 500;
    const keyboardRepeatAccelerationEnabledDefault = true;
    const defaultLayoutOverscanRows = 4;
    const defaultLayoutOverscanColumns = 2;
    const defaultCommandLogDebounceMs = 35;
    const defaultEditCommitBatchDebounceMs = 120;
    const defaultLiveRegionDebounceMs = 120;
    const nonPrimaryGestureBlockMs = 400;
    const customClipboardMime = "application/x-tempo-spreadsheet+json";

    if (window.tmSpreadsheetCanvas.keyboardRepeatAccelerationEnabled == null) {
        window.tmSpreadsheetCanvas.keyboardRepeatAccelerationEnabled = keyboardRepeatAccelerationEnabledDefault;
    }

    if (window.tmSpreadsheetCanvas.layoutOverscanRows == null) {
        window.tmSpreadsheetCanvas.layoutOverscanRows = defaultLayoutOverscanRows;
    }

    if (window.tmSpreadsheetCanvas.layoutOverscanColumns == null) {
        window.tmSpreadsheetCanvas.layoutOverscanColumns = defaultLayoutOverscanColumns;
    }

    function read(obj, name, fallback) {
        if (!obj) return fallback;
        const camel = name.charAt(0).toLowerCase() + name.slice(1);
        return obj[camel] ?? obj[name] ?? fallback;
    }

    function write(obj, name, value) {
        if (!obj) return;
        const camel = name.charAt(0).toLowerCase() + name.slice(1);
        if (camel in obj || !(name in obj)) obj[camel] = value;
        if (name in obj) obj[name] = value;
    }

    function getFormulaRuntime() {
        return window.tmSpreadsheetFormulaRuntime || window.tmSpreadsheetFormulaBar || null;
    }

    function analyzeFormulaSession(root, text, selectionStart, selectionEnd) {
        const runtime = getFormulaRuntime();
        if (runtime?.analyzeSession) {
            return runtime.analyzeSession(text, selectionStart, selectionEnd, root);
        }

        return {
            text: String(text || ""),
            selectionStart: Number(selectionStart) || 0,
            selectionEnd: Number(selectionEnd) || 0,
            isFormula: String(text || "").startsWith("="),
            isReferencePickingMode: String(text || "").startsWith("="),
            activeReferenceToken: null,
            activeReferenceTokenIndex: -1,
            referenceTokens: [],
            functionPrefix: null,
            functionPrefixStart: -1,
            functionPrefixEnd: -1,
            suggestions: [],
            activeFunctionHint: null
        };
    }


    function getHostFormulaSession(root) {
        return getFormulaRuntime()?.getHostFormulaSession?.(root) || null;
    }

    function setHostFormulaSession(root, session) {
        return getFormulaRuntime()?.setHostFormulaSession?.(root, session) || null;
    }

    function clearHostFormulaSession(root, owner) {
        return getFormulaRuntime()?.clearHostFormulaSession?.(root, owner) || null;
    }

    function forceClearHostFormulaSession(root, owner) {
        const host = root?.closest?.(".tm-spreadsheet");
        const current = host?.__tmSpreadsheetFormulaSession || null;
        if (host && (!owner || !current?.owner || current.owner === owner)) {
            delete host.__tmSpreadsheetFormulaSession;
        }
        if (owner === "inline" && host?.dataset?.formulaPointMode === "true") {
            delete host.dataset.formulaPointMode;
        }
    }

    function css(root, name, fallback) {
        return getComputedStyle(root).getPropertyValue(name).trim() || fallback;
    }

    function getState(root) {
        return root ? root[stateKey] : null;
    }

    function createSheetState(root) {
        return {
            id: "default",
            localRevision: 0,
            blazorRevision: 0,
            serverRevision: 0,
            lastLocalRevisionAt: 0,
            lastBlazorRevisionAt: 0,
            activeCell: { row: 0, col: 0, ref: "A1" },
            selection: { startRow: 0, startCol: 0, endRow: 0, endCol: 0 },
            scroll: { left: root?.scrollLeft || 0, top: root?.scrollTop || 0 },
            hover: null,
            drag: { possible: null, selection: null, autoscrollPointer: null },
            autoFill: { active: false, pointerId: 0, source: null, preview: null },
            editor: null,
            formulaEditor: {
                active: false,
                row: 0,
                col: 0,
                text: "",
                caret: 0,
                selectionStart: 0,
                selectionEnd: 0,
                tokenStart: -1,
                tokenEnd: -1,
                caretTokenStart: -1,
                caretTokenEnd: -1,
                selectionTokenStart: -1,
                selectionTokenEnd: -1,
                activeTokenIndex: -1,
                refs: [],
                suggestions: [],
                selectedSuggestionIndex: 0,
                activeFunctionHint: null,
                dragAnchor: null,
                dragCurrent: null
            },
            externalFormulaPicker: {
                active: false,
                pointerId: 0,
                anchor: null,
                current: null,
                startClientX: 0,
                startClientY: 0,
                moved: false,
                lastRefText: ""
            },
            externalFormulaOrigin: null,
            externalFormulaPointActive: false,
            formulaMode: false,
            formatPainterActive: false,
            accessibility: {
                activeCellText: "",
                liveRegionText: "",
                pendingLiveRegionText: "",
                liveRegionTimer: 0
            },
            cellStore: createCellStore(),
            layoutState: createLayoutState()
        };
    }

    function createCellStore() {
        return {
            cells: new Map(),
            formulaRefs: new Set(),
            styledOrNonEmpty: new Set(),
            merged: new Set(),
            lastVisibleKeys: new Set(),
            revision: 0,
            lastFrameCellCount: 0
        };
    }

    function createLayoutState() {
        return {
            rowCount: 0,
            columnCount: 0,
            rowSizes: new Map(),
            columnSizes: new Map(),
            columnLabels: new Map(),
            defaultRowHeight: 20,
            defaultColumnWidth: 64,
            rowOffsets: null,
            columnOffsets: null,
            rowRevision: 0,
            columnRevision: 0,
            revision: 0,
            freezeRowCount: 0,
            freezeColumnCount: 0,
            rowHeaderWidth: 40,
            columnHeaderHeight: 20
        };
    }

    function createRendererState(canvas, headerCanvas, selectionCanvas, root) {
        return {
            layers: {
                content: { canvas, dirty: true },
                header: { canvas: headerCanvas || null, dirty: true },
                selection: { canvas: selectionCanvas || null, dirty: true },
                editor: { element: root || null, dirty: false }
            },
            dirty: {
                content: true,
                header: true,
                selection: true,
                editor: false,
                full: true
            },
            dirtyRects: {
                content: [],
                header: [],
                selection: []
            },
            cache: {
                textMetrics: new Map(),
                fonts: new Map(),
                paintStyles: new Map(),
                displayValues: new Map(),
                cellSnapshots: new Map()
            }
        };
    }

    function createWorkbookState(root) {
        const sheet = createSheetState(root);
        return {
            localRevision: 0,
            blazorRevision: 0,
            serverRevision: 0,
            activeSheetId: sheet.id,
            sheets: { [sheet.id]: sheet }
        };
    }

    function getSheetState(root) {
        return getState(root)?.sheetState || null;
    }

    function bumpLocalRevision(root, source) {
        const s = getState(root);
        const sheet = s?.sheetState;
        if (!s || !sheet) return 0;

        const next = Math.max((s.interactionVersion || 0) + 1, (sheet.localRevision || 0) + 1);
        s.interactionVersion = next;
        s.lastInteractionSource = source || "local";
        sheet.localRevision = next;
        sheet.lastLocalRevisionAt = performance.now();
        if (s.workbookState) s.workbookState.localRevision = next;
        return next;
    }

    function setSheetScroll(root, left, top) {
        const sheet = getSheetState(root);
        if (!sheet) return;
        sheet.scroll.left = Number(left) || 0;
        sheet.scroll.top = Number(top) || 0;
    }

    function setSheetSelection(root, row, col, startRow, startCol, endRow, endCol) {
        const sheet = getSheetState(root);
        if (!sheet) return;
        sheet.activeCell = { row, col, ref: toCellRef(row, col) };
        sheet.selection = { startRow, startCol, endRow, endCol };
        syncAccessibilityState(root, false);
    }

    function ensureActiveCellDomProxy(root) {
        const s = getState(root);
        if (!root || !s) return null;
        if (s.activeCellDomProxy?.isConnected) return s.activeCellDomProxy;

        const proxy = document.createElement("div");
        proxy.className = "tm-spreadsheet-cell tm-spreadsheet-cell--active tm-spreadsheet-canvas-grid__active-cell-proxy";
        proxy.setAttribute("aria-hidden", "true");
        const focusRoot = ev => {
            root.focus?.({ preventScroll: true });
            ev.preventDefault();
            ev.stopPropagation();
        };
        proxy.addEventListener("pointerdown", focusRoot);
        proxy.addEventListener("mousedown", focusRoot);
        proxy.addEventListener("click", focusRoot);
        root.appendChild(proxy);
        s.activeCellDomProxy = proxy;
        return proxy;
    }

    function ensureDomCellProxyLayer(root) {
        const s = getState(root);
        if (!root || !s) return null;
        if (s.cellDomProxyLayer?.isConnected) return s.cellDomProxyLayer;

        const layer = document.createElement("div");
        layer.className = "tm-spreadsheet-canvas-grid__cell-proxy-layer";
        layer.setAttribute("aria-hidden", "true");
        root.appendChild(layer);
        s.cellDomProxyLayer = layer;
        return layer;
    }

    function updateDomCellProxies(root) {
        const s = getState(root);
        const model = s?.model;
        if (!root || !s || !model) return;

        const layer = ensureDomCellProxyLayer(root);
        if (!layer) return;

        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        const layout = getVisibleLayout(root, model, width, height);
        const visibleRefs = new Set();
        const row = 0;
        const activeRef = String(read(model, "ActiveCellRef", "A1") || "A1").toUpperCase();

        for (const colFrame of layout.columns || []) {
            const col = read(colFrame, "Index", -1);
            if (col < 0) continue;
            const ref = toCellRef(row, col);
            if (ref.toUpperCase() === activeRef) continue;
            visibleRefs.add(ref);

            let proxy = layer.querySelector(`[data-cell-ref="${ref}"]`);
            if (!proxy) {
                proxy = document.createElement("div");
                proxy.className = "tm-spreadsheet-cell tm-spreadsheet-canvas-grid__cell-proxy";
                proxy.dataset.cellRef = ref;
                proxy.title = ref;
                layer.appendChild(proxy);
            }

            proxy.hidden = false;
        }

        for (const proxy of layer.querySelectorAll(".tm-spreadsheet-canvas-grid__cell-proxy")) {
            const ref = proxy.dataset.cellRef || "";
            if (!visibleRefs.has(ref)) proxy.hidden = true;
        }
    }

    function updateActiveCellDomProxy(root) {
        const s = getState(root);
        const model = s?.model;
        if (!root || !s || !model) return;

        const active = parseCellRef(read(model, "ActiveCellRef", s.sheetState?.activeCell?.ref || "A1"));
        const proxy = ensureActiveCellDomProxy(root);
        if (!proxy) return;

        const ref = toCellRef(active.row, active.col);
        proxy.title = ref;
        proxy.dataset.cellRef = ref;
        proxy.textContent = read(findCell(model, active.row, active.col), "Value", "") || "";

        proxy.hidden = false;
        proxy.style.left = `${(root.scrollLeft || 0) + 1}px`;
        proxy.style.top = `${(root.scrollTop || 0) + 1}px`;
        proxy.style.width = "24px";
        proxy.style.height = "18px";
        updateDomCellProxies(root);
    }

    function setSheetHover(root, hover) {
        const sheet = getSheetState(root);
        if (sheet) sheet.hover = hover;
        const s = getState(root);
        if (s) s.hoverCell = hover;
    }

    function getSheetHover(root) {
        const s = getState(root);
        return s?.sheetState?.hover ?? s?.hoverCell ?? null;
    }

    function setPossibleDrag(root, drag) {
        const s = getState(root);
        if (!s) return;
        s.possibleDrag = drag;
        if (s.sheetState) s.sheetState.drag.possible = drag;
    }

    function getPossibleDrag(root) {
        const s = getState(root);
        return s?.sheetState?.drag?.possible ?? s?.possibleDrag ?? null;
    }

    function setSelectionDrag(root, drag) {
        const s = getState(root);
        if (!s) return;
        s.selectionDrag = drag;
        if (s.sheetState) s.sheetState.drag.selection = drag;
    }

    function getSelectionDrag(root) {
        const s = getState(root);
        return s?.sheetState?.drag?.selection ?? s?.selectionDrag ?? null;
    }

    function setAutoFillState(root, autoFill) {
        const s = getState(root);
        if (!s) return;
        s.autoFill = autoFill;
        if (s.sheetState) s.sheetState.autoFill = autoFill;
    }

    function getAutoFillState(root) {
        const s = getState(root);
        return s?.sheetState?.autoFill ?? s?.autoFill ?? null;
    }

    function setDragAutoscrollPointer(root, pointer) {
        const s = getState(root);
        if (!s) return;
        s.dragAutoscrollPointer = pointer;
        if (s.sheetState) s.sheetState.drag.autoscrollPointer = pointer;
    }

    function setSheetEditor(root, editor) {
        const s = getState(root);
        if (!s) return;
        s.editor = editor;
        if (!s.sheetState) return;
        s.sheetState.editor = editor
            ? {
                row: editor.row,
                col: editor.col,
                value: editor.input?.value ?? "",
                initialValue: editor.initialValue ?? "",
                revision: s.sheetState.localRevision || s.interactionVersion || 0
            }
            : null;
        syncEditorAccessibility(root);
        syncAccessibilityState(root, false);
    }

    function updateSheetEditorValue(root) {
        const s = getState(root);
        const editor = s?.editor;
        if (!s?.sheetState || !editor) return;
        s.sheetState.editor = {
            row: editor.row,
            col: editor.col,
            value: editor.input?.value ?? "",
            initialValue: editor.initialValue ?? "",
            revision: s.sheetState.localRevision || s.interactionVersion || 0
        };
        syncEditorAccessibility(root);
        syncAccessibilityState(root, false);
    }

    function getAccessibilityState(root) {
        const s = getState(root);
        if (!s?.sheetState) return null;
        if (!s.sheetState.accessibility) {
            s.sheetState.accessibility = {
                activeCellText: "",
                liveRegionText: "",
                pendingLiveRegionText: "",
                liveRegionTimer: 0
            };
        }
        return s.sheetState.accessibility;
    }

    function getA11yElement(root, datasetKey) {
        const id = root?.dataset?.[datasetKey];
        return id ? document.getElementById(id) : null;
    }

    function getActiveCellState(root) {
        const sheet = getSheetState(root);
        if (sheet?.activeCell) {
            const row = Number(sheet.activeCell.row);
            const col = Number(sheet.activeCell.col);
            if (Number.isFinite(row) && Number.isFinite(col)) {
                return { row, col, ref: sheet.activeCell.ref || toCellRef(row, col) };
            }
        }

        const active = parseCellRef(read(getState(root)?.model, "ActiveCellRef", "A1"));
        return { row: active.row, col: active.col, ref: toCellRef(active.row, active.col) };
    }

    function getCellAccessibilityValue(root, row, col) {
        const s = getState(root);
        const editor = s?.editor;
        if (editor && editor.row === row && editor.col === col) {
            return editor.input?.value || "";
        }

        const storeCell = getCellStore(root)?.cells?.get(cellStoreKey(row, col));
        const modelCell = storeCell || findCell(s?.model, row, col);
        if (!modelCell) return "";

        const formula = read(modelCell, "Formula", null);
        if (typeof formula === "string" && formula.length > 0) return formula;

        const displayValue = read(modelCell, "DisplayValue", null);
        if (displayValue != null && `${displayValue}`.length > 0) return `${displayValue}`;

        const value = read(modelCell, "Value", "");
        return value == null ? "" : `${value}`;
    }

    function buildAccessibilityActiveCellText(root) {
        const active = getActiveCellState(root);
        const prefix = root?.dataset?.a11yActiveCellPrefix || "";
        const value = getCellAccessibilityValue(root, active.row, active.col).trim();
        return [prefix, active.ref || toCellRef(active.row, active.col), value].filter(part => !!part).join(" ").trim();
    }

    function syncActiveCellProxy(root) {
        const activeElement = getA11yElement(root, "a11yActiveCellId");
        const active = getActiveCellState(root);
        const text = buildAccessibilityActiveCellText(root) || active.ref || "A1";
        const accessibility = getAccessibilityState(root);
        if (accessibility) accessibility.activeCellText = text;
        if (!activeElement) return text;

        activeElement.textContent = text;
        activeElement.setAttribute("aria-rowindex", String(active.row + 1));
        activeElement.setAttribute("aria-colindex", String(active.col + 1));
        activeElement.setAttribute("aria-selected", "true");
        if (root?.dataset?.a11yActiveCellId) {
            root.setAttribute("aria-activedescendant", root.dataset.a11yActiveCellId);
        }

        return text;
    }

    function flushLiveRegion(root) {
        const accessibility = getAccessibilityState(root);
        const liveRegion = getA11yElement(root, "a11yLiveRegionId");
        if (!accessibility || !liveRegion) return;

        if (accessibility.liveRegionTimer) {
            clearTimeout(accessibility.liveRegionTimer);
            accessibility.liveRegionTimer = 0;
        }

        const text = accessibility.pendingLiveRegionText || syncActiveCellProxy(root);
        accessibility.pendingLiveRegionText = text;
        if (accessibility.liveRegionText === text) {
            liveRegion.textContent = "";
            setTimeout(() => {
                if (!getState(root)) return;
                liveRegion.textContent = text;
            }, 0);
        } else {
            liveRegion.textContent = text;
        }
        accessibility.liveRegionText = text;
    }

    function scheduleLiveRegionUpdate(root, delay) {
        const accessibility = getAccessibilityState(root);
        if (!accessibility) return;

        accessibility.pendingLiveRegionText = syncActiveCellProxy(root);
        if (accessibility.liveRegionTimer) {
            clearTimeout(accessibility.liveRegionTimer);
        }

        accessibility.liveRegionTimer = setTimeout(() => {
            accessibility.liveRegionTimer = 0;
            flushLiveRegion(root);
        }, delay ?? defaultLiveRegionDebounceMs);
    }

    function syncAccessibilityState(root, announceMode) {
        syncActiveCellProxy(root);
        if (announceMode === false) return;
        if (announceMode === "immediate") {
            flushLiveRegion(root);
            return;
        }

        scheduleLiveRegionUpdate(root, typeof announceMode === "number" ? announceMode : defaultLiveRegionDebounceMs);
    }

    function syncEditorAccessibility(root) {
        const editor = getState(root)?.editor;
        const input = editor?.input;
        if (!input) return;

        const isFormula = (input.value || "").startsWith("=") || !!getState(root)?.sheetState?.formulaEditor?.active;
        const baseLabel = isFormula
            ? (root?.dataset?.a11yFormulaEditorLabel || root?.dataset?.a11yCellEditorLabel || "Formula editor")
            : (root?.dataset?.a11yCellEditorLabel || "Cell editor");
        const describedBy = root?.dataset?.a11yDescriptionId || "";
        const ref = toCellRef(editor.row, editor.col);

        input.type = "text";
        input.setAttribute("role", "textbox");
        input.setAttribute("spellcheck", "false");
        input.setAttribute("autocomplete", "off");
        input.setAttribute("autocorrect", "off");
        input.setAttribute("autocapitalize", "off");
        input.setAttribute("aria-multiline", "false");
        input.setAttribute("aria-label", `${baseLabel} ${ref}`.trim());
        if (describedBy) input.setAttribute("aria-describedby", describedBy);
        else input.removeAttribute("aria-describedby");
        input.dataset.editorMode = isFormula ? "formula" : "cell";
    }

    function isJsEngine(root) {
        const model = getState(root)?.model;
        return !!read(model, "UseJsEngine", false);
    }

    function isHostFormulaPointMode(root) {
        return !!getFormulaRuntime()?.isHostFormulaPointMode?.(root);
    }

    function hasLiveFormulaBarFormulaSession(root) {
        const host = root?.closest?.(".tm-spreadsheet");
        const input = host?.querySelector?.(".tm-spreadsheet-formula-bar__input");
        return !!(input instanceof HTMLInputElement
            && input.offsetParent !== null
            && String(input.value || "").startsWith("="));
    }

    function hasRecentFormulaBarFormulaSession(root) {
        const host = root?.closest?.(".tm-spreadsheet");
        return performance.now() < Number(host?.__tmSpreadsheetFormulaRecentUntil || 0);
    }

    function hasDurableFormulaGuard(root) {
        const host = root?.closest?.(".tm-spreadsheet");
        return host?.dataset?.formulaGuardActive === "true";
    }

    function shouldGuardFormulaGesture(root) {
        return isFormulaPointMode(root)
            || hasDurableFormulaGuard(root)
            || hasLiveFormulaBarFormulaSession(root)
            || hasRecentFormulaBarFormulaSession(root);
    }

    function getExternalFormulaSession(root) {
        const session = getHostFormulaSession(root);
        if (!session || !session.isFormula) return null;
        if (!isHostFormulaPointMode(root)) return null;
        const localEditor = getState(root)?.editor;
        if (localEditor) return null;
        return session;
    }

    function isFormulaPointMode(root) {
        const s = getState(root);
        return !!(
            s?.sheetState?.formulaEditor?.active
            || s?.sheetState?.formulaMode
            || hasDurableFormulaGuard(root)
            || root?.dataset?.externalFormulaSessionActive === "true"
            || !!root?.dataset?.externalFormulaOriginRef
            || read(s?.model, "ExternalFormulaSessionActive", false)
            || !!read(s?.model, "ExternalFormulaOriginCellRef", "")
            || read(s?.model, "IsFormulaPointMode", false)
            || isHostFormulaPointMode(root)
            || hasLiveFormulaBarFormulaSession(root));
    }

    function isFormatPainterActive(root) {
        const s = getState(root);
        return !!(s?.sheetState?.formatPainterActive || read(s?.model, "IsFormatPainterActive", false));
    }

    function cellStoreKey(row, col) {
        return `${Number(row) || 0}:${Number(col) || 0}`;
    }

    function cellStoreKeyFromCell(cell) {
        return cellStoreKey(read(cell, "Row", 0), read(cell, "Col", 0));
    }

    function hasCellStylePayload(style) {
        if (!style) return false;
        for (const key of [
            "FontFamily", "FontSize", "Bold", "Italic", "Underline", "DoubleUnderline", "StrikeThrough",
            "ForeColor", "BackgroundColor", "HorizontalAlign", "VerticalAlign", "NumberFormat", "TextWrap",
            "BorderTop", "BorderRight", "BorderBottom", "BorderLeft"
        ]) {
            const value = read(style, key, null);
            if (value !== null && value !== undefined && value !== false && value !== "" && value !== "general") {
                return true;
            }
        }

        return false;
    }

    function isStyledOrNonEmptyCell(cell) {
        return !!(
            read(cell, "Value", "") ||
            read(cell, "ImageUrl", "") ||
            read(cell, "Hyperlink", "") ||
            hasCellStylePayload(read(cell, "Style", null))
        );
    }

    function buildFrameSizeMaps(model) {
        const rows = new Map();
        const columns = new Map();
        for (const row of read(model, "Rows", [])) {
            rows.set(read(row, "Index", -1), read(row, "Height", 0));
        }

        for (const column of read(model, "Columns", [])) {
            columns.set(read(column, "Index", -1), read(column, "Width", 0));
        }

        return { rows, columns };
    }

    function isMergedStoreCell(cell, frameSizes) {
        if (read(cell, "Merged", false) || read(cell, "IsMerged", false)) return true;
        const row = read(cell, "Row", -1);
        const col = read(cell, "Col", -1);
        const normalHeight = frameSizes?.rows?.get(row) || 0;
        const normalWidth = frameSizes?.columns?.get(col) || 0;
        return (normalHeight > 0 && read(cell, "Height", normalHeight) > normalHeight + 0.5)
            || (normalWidth > 0 && read(cell, "Width", normalWidth) > normalWidth + 0.5);
    }

    function indexStoreCell(store, key, cell, frameSizes) {
        store.formulaRefs.delete(key);
        store.styledOrNonEmpty.delete(key);
        store.merged.delete(key);

        if (Number(read(cell, "FormulaRefColorIndex", -1)) >= 0) store.formulaRefs.add(key);
        if (isStyledOrNonEmptyCell(cell)) store.styledOrNonEmpty.add(key);
        if (isMergedStoreCell(cell, frameSizes)) store.merged.add(key);
    }

    function mergeCellPatch(existing, patch) {
        if (!existing) return patch;
        for (const [key, value] of Object.entries(patch || {})) {
            write(existing, key, value);
        }
        return existing;
    }

    function mergeAxisFrames(existingFrames, patchFrames) {
        const existing = Array.isArray(existingFrames) ? existingFrames.slice() : [];
        const byIndex = new Map();
        for (const frame of existing) {
            byIndex.set(read(frame, "Index", -1), frame);
        }

        for (const patch of patchFrames || []) {
            const index = read(patch, "Index", -1);
            if (index < 0) continue;
            const current = byIndex.get(index);
            byIndex.set(index, current ? mergeCellPatch(current, { ...patch }) : { ...patch });
        }

        return [...byIndex.values()].sort((left, right) => read(left, "Index", 0) - read(right, "Index", 0));
    }

    function syncCellStoreFromModel(root, model, options) {
        const s = getState(root);
        const store = s?.sheetState?.cellStore;
        if (!s || !store || !model) return;

        modelRootMap.set(model, root);
        const cells = read(model, "Cells", []);
        const settings = options || {};
        const allowOverwrite = settings.allowOverwrite !== false;
        const frameSizes = buildFrameSizeMaps(model);
        const visibleKeys = new Set();
        let changed = false;

        for (const cell of cells) {
            const key = cellStoreKeyFromCell(cell);
            visibleKeys.add(key);
            if (allowOverwrite || !store.cells.has(key)) {
                store.cells.set(key, cell);
                changed = true;
            }
            indexStoreCell(store, key, store.cells.get(key), frameSizes);
        }

        store.lastVisibleKeys = visibleKeys;
        store.lastFrameCellCount = cells.length;
        if (changed || allowOverwrite) store.revision += 1;
        if (s.metrics) {
            s.metrics.cellStoreSize = store.cells.size;
            s.metrics.cellStoreRevision = store.revision;
            s.metrics.cellStoreFormulaRefCount = store.formulaRefs.size;
            s.metrics.cellStoreStyledOrNonEmptyCount = store.styledOrNonEmpty.size;
            s.metrics.cellStoreMergedCount = store.merged.size;
            s.metrics.cellStoreLastFrameCellCount = cells.length;
        }
    }

    function getCellStore(root) {
        return getState(root)?.sheetState?.cellStore || null;
    }

    function setStoreCells(root, cells, options) {
        const s = getState(root);
        const store = getCellStore(root);
        if (!s || !store || !Array.isArray(cells) || cells.length === 0) return 0;

        const frameSizes = buildFrameSizeMaps(s.model || {});
        const modelCells = read(s.model, "Cells", []);
        let changed = 0;
        for (const patch of cells) {
            if (!patch) continue;
            const row = read(patch, "Row", null);
            const col = read(patch, "Col", null);
            const parsed = row == null || col == null ? parseCellRef(read(patch, "Ref", "A1")) : null;
            const nextRow = row == null ? parsed.row : Number(row) || 0;
            const nextCol = col == null ? parsed.col : Number(col) || 0;
            write(patch, "Row", nextRow);
            write(patch, "Col", nextCol);
            write(patch, "Ref", read(patch, "Ref", toCellRef(nextRow, nextCol)));
            const key = cellStoreKey(nextRow, nextCol);
            const existing = store.cells.get(key) || modelCells.find(cell => read(cell, "Row", -1) === nextRow && read(cell, "Col", -1) === nextCol);
            const cell = mergeCellPatch(existing, patch);
            store.cells.set(key, cell);
            indexStoreCell(store, key, cell, frameSizes);
            invalidateCellSnapshot(root, nextRow, nextCol);
            changed += 1;
        }

        store.revision += 1;
        bumpLocalRevision(root, "cell-store");
        if (s.metrics) {
            s.metrics.cellStoreSetCellCount += changed;
            s.metrics.cellStoreSize = store.cells.size;
            s.metrics.cellStoreRevision = store.revision;
            s.metrics.cellStoreFormulaRefCount = store.formulaRefs.size;
            s.metrics.cellStoreStyledOrNonEmptyCount = store.styledOrNonEmpty.size;
            s.metrics.cellStoreMergedCount = store.merged.size;
        }
        syncAccessibilityState(root, false);
        if (!options?.suppressRedraw) {
            requestCanvasRedraw(root, "cell-store", "content");
        }
        if (options?.queueRangeCommand && changed > 0) {
            const payload = cells.map(patch => ({
                row: read(patch, "Row", 0),
                col: read(patch, "Col", 0),
                value: toCommandCellValue(read(patch, "Formula", null) || read(patch, "Value", "")),
                interactionVersion: currentInteractionVersion(root)
            }));
            queueCommand(root, "rangeChanged", { cellEdits: payload }, { delay: options?.commandDelay ?? defaultCommandLogDebounceMs });
        }
        return changed;
    }

    function clearStoreCells(root, cells, options) {
        const s = getState(root);
        const store = getCellStore(root);
        if (!s || !store || !Array.isArray(cells) || cells.length === 0) return 0;

        const frameSizes = buildFrameSizeMaps(s.model || {});
        let changed = 0;
        for (const item of cells) {
            const parsed = typeof item === "string"
                ? parseCellRef(item)
                : {
                    row: read(item, "Row", parseCellRef(read(item, "Ref", "A1")).row),
                    col: read(item, "Col", parseCellRef(read(item, "Ref", "A1")).col)
                };
            const key = cellStoreKey(parsed.row, parsed.col);
            const cell = store.cells.get(key);
            if (cell) {
                write(cell, "Value", "");
                write(cell, "ImageUrl", null);
                write(cell, "Hyperlink", null);
                write(cell, "Style", null);
                write(cell, "FormulaRefColorIndex", -1);
                indexStoreCell(store, key, cell, frameSizes);
            }
            invalidateCellSnapshot(root, parsed.row, parsed.col);
            changed += 1;
        }

        store.revision += 1;
        bumpLocalRevision(root, "cell-store");
        if (s.metrics) {
            s.metrics.cellStoreClearCellCount += changed;
            s.metrics.cellStoreRevision = store.revision;
            s.metrics.cellStoreFormulaRefCount = store.formulaRefs.size;
            s.metrics.cellStoreStyledOrNonEmptyCount = store.styledOrNonEmpty.size;
            s.metrics.cellStoreMergedCount = store.merged.size;
        }
        syncAccessibilityState(root, false);
        requestCanvasRedraw(root, "cell-store", "content");
        if (options?.queueRangeCommand && changed > 0) {
            const payload = cells.map(item => {
                const parsed = typeof item === "string"
                    ? parseCellRef(item)
                    : {
                        row: read(item, "Row", parseCellRef(read(item, "Ref", "A1")).row),
                        col: read(item, "Col", parseCellRef(read(item, "Ref", "A1")).col)
                    };
                return {
                    row: parsed.row,
                    col: parsed.col,
                    value: "",
                    interactionVersion: currentInteractionVersion(root)
                };
            });
            queueCommand(root, "rangeChanged", { cellEdits: payload }, { delay: options?.commandDelay ?? defaultCommandLogDebounceMs });
        }
        return changed;
    }

    function getLayoutState(root) {
        return getState(root)?.sheetState?.layoutState || null;
    }

    function layoutOverscanRows() {
        return Math.max(0, Number(window.tmSpreadsheetCanvas.layoutOverscanRows ?? defaultLayoutOverscanRows) || 0);
    }

    function layoutOverscanColumns() {
        return Math.max(0, Number(window.tmSpreadsheetCanvas.layoutOverscanColumns ?? defaultLayoutOverscanColumns) || 0);
    }

    function updateAxisCache(layout, axis, frames, count, defaultSize, sizeName, indexName, labelName) {
        const sizeMap = axis === "row" ? layout.rowSizes : layout.columnSizes;
        const labelMap = layout.columnLabels;
        let changed = false;
        let sizeTotal = 0;
        let sizeCount = 0;

        for (const frame of frames || []) {
            const index = read(frame, indexName, -1);
            const size = Number(read(frame, sizeName, 0)) || 0;
            if (index < 0) continue;
            // A size of 0 means a hidden row/column: record it (so the layout collapses it) but keep
            // it out of the default-size average. Negative sizes are ignored.
            if (size < 0) continue;
            if (sizeMap.get(index) !== size) {
                sizeMap.set(index, size);
                changed = true;
            }
            if (size > 0) {
                sizeTotal += size;
                sizeCount += 1;
                if (axis === "column") {
                    const label = read(frame, labelName, "");
                    if (label && labelMap.get(index) !== label) labelMap.set(index, label);
                }
            }
        }

        if (sizeCount > 0) {
            const nextDefault = Math.max(1, sizeTotal / sizeCount);
            const roundedNext = Math.round(nextDefault * 100) / 100;
            const current = axis === "row" ? layout.defaultRowHeight : layout.defaultColumnWidth;
            if (Math.abs(current - roundedNext) > 0.5) {
                if (axis === "row") layout.defaultRowHeight = roundedNext;
                else layout.defaultColumnWidth = roundedNext;
                changed = true;
            }
        } else if (defaultSize > 0) {
            const current = axis === "row" ? layout.defaultRowHeight : layout.defaultColumnWidth;
            if (Math.abs(current - defaultSize) > 0.5) {
                if (axis === "row") layout.defaultRowHeight = defaultSize;
                else layout.defaultColumnWidth = defaultSize;
                changed = true;
            }
        }

        if (axis === "row" && layout.rowCount !== count) {
            layout.rowCount = count;
            changed = true;
        } else if (axis === "column" && layout.columnCount !== count) {
            layout.columnCount = count;
            changed = true;
        }

        if (changed) {
            if (axis === "row") {
                layout.rowOffsets = null;
                layout.rowRevision += 1;
            } else {
                layout.columnOffsets = null;
                layout.columnRevision += 1;
            }
            layout.revision += 1;
        }

        return changed;
    }

    function syncLayoutStateFromModel(root, model) {
        const s = getState(root);
        const layout = getLayoutState(root);
        if (!s || !layout || !model) return;

        const rows = read(model, "Rows", []);
        const columns = read(model, "Columns", []);
        const rowCount = read(model, "RowCount", rows.length);
        const columnCount = read(model, "ColumnCount", columns.length);
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const freezeRowCount = read(model, "FreezeRowCount", 0);
        const freezeColumnCount = read(model, "FreezeColumnCount", 0);
        let changed = false;

        changed = updateAxisCache(layout, "row", rows, rowCount, layout.defaultRowHeight, "Height", "Index") || changed;
        changed = updateAxisCache(layout, "column", columns, columnCount, layout.defaultColumnWidth, "Width", "Index", "Label") || changed;

        if (layout.rowHeaderWidth !== rowHeaderWidth || layout.columnHeaderHeight !== columnHeaderHeight) {
            layout.rowHeaderWidth = rowHeaderWidth;
            layout.columnHeaderHeight = columnHeaderHeight;
            changed = true;
        }

        if (layout.freezeRowCount !== freezeRowCount || layout.freezeColumnCount !== freezeColumnCount) {
            layout.freezeRowCount = freezeRowCount;
            layout.freezeColumnCount = freezeColumnCount;
            changed = true;
            if (s.metrics) s.metrics.visibleLayoutFreezeInvalidationCount += 1;
        }

        if (changed) invalidateVisibleLayout(root, "layout-state");
        if (s.metrics) {
            s.metrics.layoutRowSizeCacheSize = layout.rowSizes.size;
            s.metrics.layoutColumnSizeCacheSize = layout.columnSizes.size;
            s.metrics.layoutRevision = layout.revision;
            s.metrics.layoutOverscanRows = layoutOverscanRows();
            s.metrics.layoutOverscanColumns = layoutOverscanColumns();
        }
    }

    function invalidateVisibleLayout(root, reason) {
        const s = getState(root);
        if (!s) return;
        if (s.visibleLayoutCache && s.metrics) s.metrics.visibleLayoutInvalidationCount += 1;
        s.visibleLayoutCache = null;
        s.visibleLayoutInvalidationReason = reason || "";
    }

    function setLayoutAxisSize(root, axis, index, size) {
        const layout = getLayoutState(root);
        const s = getState(root);
        if (!layout) return false;
        const next = Math.max(axis === "column" ? 16 : 8, Number(size) || 0);
        const map = axis === "column" ? layout.columnSizes : layout.rowSizes;
        if (Math.abs((map.get(index) || 0) - next) <= 0.5) return false;
        map.set(index, next);
        if (axis === "column") {
            layout.columnOffsets = null;
            layout.columnRevision += 1;
            invalidateColumnSnapshots(root, index);
        } else {
            layout.rowOffsets = null;
            layout.rowRevision += 1;
            invalidateRowSnapshots(root, index);
        }
        layout.revision += 1;
        invalidateVisibleLayout(root, "resize");
        if (s?.metrics) s.metrics.visibleLayoutResizeInvalidationCount += 1;
        return true;
    }

    function buildAxisOffsets(count, defaultSize, sizeMap) {
        const offsets = new Float64Array(Math.max(0, count) + 1);
        for (let i = 0; i < count; i++) {
            // A stored size of 0 is a hidden row/column and must collapse — do not fall back to the
            // default via `||`, which would treat the meaningful 0 as "unset".
            const sz = sizeMap.get(i);
            offsets[i + 1] = offsets[i] + (sz === undefined ? defaultSize : sz);
        }
        return offsets;
    }

    function getRowOffsets(layout) {
        if (!layout.rowOffsets) {
            layout.rowOffsets = buildAxisOffsets(layout.rowCount, layout.defaultRowHeight, layout.rowSizes);
        }
        return layout.rowOffsets;
    }

    function getColumnOffsets(layout) {
        if (!layout.columnOffsets) {
            layout.columnOffsets = buildAxisOffsets(layout.columnCount, layout.defaultColumnWidth, layout.columnSizes);
        }
        return layout.columnOffsets;
    }

    function binarySearchOffset(offsets, value) {
        if (!offsets || offsets.length <= 1) return 0;
        let lo = 0;
        let hi = offsets.length - 2;
        const target = Math.max(0, Number(value) || 0);
        while (lo <= hi) {
            const mid = (lo + hi) >> 1;
            if (offsets[mid + 1] <= target) lo = mid + 1;
            else if (offsets[mid] > target) hi = mid - 1;
            else return mid;
        }
        return Math.max(0, Math.min(offsets.length - 2, lo));
    }

    function createRowFrame(layout, index, scrollTop, frozen) {
        const offsets = getRowOffsets(layout);
        const top = offsets[index] || 0;
        const height = Math.max(0, offsets[index + 1] - top);
        return {
            index,
            Index: index,
            top,
            Top: top,
            y: layout.columnHeaderHeight + top - (frozen ? 0 : scrollTop),
            height,
            Height: height,
            frozen,
            Frozen: frozen
        };
    }

    function createColumnFrame(layout, index, scrollLeft, frozen) {
        const offsets = getColumnOffsets(layout);
        const left = offsets[index] || 0;
        const width = Math.max(0, offsets[index + 1] - left);
        const label = layout.columnLabels.get(index) || columnIndexToLetters(index);
        return {
            index,
            Index: index,
            left,
            Left: left,
            x: layout.rowHeaderWidth + left - (frozen ? 0 : scrollLeft),
            width,
            Width: width,
            label,
            Label: label,
            frozen,
            Frozen: frozen
        };
    }

    function columnIndexToLetters(index) {
        let n = Math.max(0, Number(index) || 0) + 1;
        let letters = "";
        while (n > 0) {
            const rem = (n - 1) % 26;
            letters = String.fromCharCode(65 + rem) + letters;
            n = Math.floor((n - 1) / 26);
        }
        return letters;
    }

    function columnLettersToIndex(letters) {
        let col = 0;
        for (const ch of String(letters || "").toUpperCase()) {
            col = col * 26 + ch.charCodeAt(0) - 64;
        }
        return Math.max(0, col - 1);
    }

    function safeSetPointerCapture(root, pointerId) {
        try { root?.setPointerCapture?.(pointerId); } catch { }
    }

    function safeReleasePointerCapture(root, pointerId) {
        try { root?.releasePointerCapture?.(pointerId); } catch { }
    }

    function limitCache(cache, maxSize) {
        if (!cache || cache.size <= maxSize) return;
        while (cache.size > maxSize) {
            const firstKey = cache.keys().next().value;
            cache.delete(firstKey);
        }
    }

    function maxLogicalScrollLeft(root) {
        return Math.max(0, (root?.scrollWidth || 0) - (root?.clientWidth || 0));
    }

    function maxLogicalScrollTop(root) {
        return Math.max(0, (root?.scrollHeight || 0) - (root?.clientHeight || 0));
    }

    function clampLogicalScrollLeft(root, value) {
        return Math.max(0, Math.min(maxLogicalScrollLeft(root), Number(value) || 0));
    }

    function clampLogicalScrollTop(root, value) {
        return Math.max(0, Math.min(maxLogicalScrollTop(root), Number(value) || 0));
    }

    function getLogicalScrollLeft(root) {
        const s = getState(root);
        return s
            ? (Number.isFinite(s.logicalScrollLeft) ? s.logicalScrollLeft : s.sheetState?.scroll?.left ?? 0)
            : root?.scrollLeft || 0;
    }

    function getLogicalScrollTop(root) {
        const s = getState(root);
        return s
            ? (Number.isFinite(s.logicalScrollTop) ? s.logicalScrollTop : s.sheetState?.scroll?.top ?? 0)
            : root?.scrollTop || 0;
    }

    function setLogicalScroll(root, left, top, source) {
        const s = getState(root);
        if (!s) return false;
        const nextLeft = clampLogicalScrollLeft(root, left);
        const nextTop = clampLogicalScrollTop(root, top);
        const changed = Math.abs(nextLeft - (s.logicalScrollLeft || 0)) > 0.5
            || Math.abs(nextTop - (s.logicalScrollTop || 0)) > 0.5;
        s.logicalScrollLeft = nextLeft;
        s.logicalScrollTop = nextTop;
        setSheetScroll(root, nextLeft, nextTop);
        if (s.model) {
            write(s.model, "ScrollLeft", nextLeft);
            write(s.model, "ScrollTop", nextTop);
        }
        if (changed) updateLocalEditorPosition(root);
        if (changed) updateActiveCellDomProxy(root);

        if (changed) bumpLocalRevision(root, source || "scroll");

        if (changed && source === "keyboard") {
            s.lastLogicalKeyboardScrollAt = performance.now();
        }

        if (s.metrics) {
            s.metrics.logicalScrollLeft = nextLeft;
            s.metrics.logicalScrollTop = nextTop;
            s.metrics.nativeScrollLeft = root.scrollLeft || 0;
            s.metrics.nativeScrollTop = root.scrollTop || 0;
            if (changed) {
                if (source === "keyboard") {
                    s.metrics.logicalKeyboardScrollCount += 1;
                } else if (source === "native") {
                    s.metrics.logicalNativeScrollCount += 1;
                } else if (source === "programmatic") {
                    s.metrics.logicalProgrammaticScrollCount += 1;
                } else if (source === "wheel") {
                    s.metrics.logicalWheelScrollCount += 1;
                } else if (source === "pointer") {
                    s.metrics.logicalPointerScrollCount += 1;
                }
            }
        }

        return changed;
    }

    function syncLogicalScrollFromNative(root, source) {
        const s = getState(root);
        if (!s) return false;
        return setLogicalScroll(root, root.scrollLeft || 0, root.scrollTop || 0, source || "native");
    }

    function syncModelViewport(root, model) {
        write(model, "ScrollLeft", getLogicalScrollLeft(root));
        write(model, "ScrollTop", getLogicalScrollTop(root));
        write(model, "ViewportWidth", root.clientWidth || read(model, "ViewportWidth", 1));
        write(model, "ViewportHeight", root.clientHeight || read(model, "ViewportHeight", 1));
    }

    function syncNativeScrollFromLogical(root) {
        const s = getState(root);
        if (!s) return;
        const left = clampLogicalScrollLeft(root, s.logicalScrollLeft || 0);
        const top = clampLogicalScrollTop(root, s.logicalScrollTop || 0);
        if (Math.abs((root.scrollLeft || 0) - left) <= 0.5 && Math.abs((root.scrollTop || 0) - top) <= 0.5) {
            return;
        }

        s.ownScrollUntil = performance.now() + 250;
        if (s.metrics) {
            s.metrics.scrollToCount += 1;
            s.metrics.logicalNativeSyncCount += 1;
        }
        root.scrollTo({ left, top, behavior: "auto" });
    }

    function scheduleNativeScrollSync(root, delay) {
        const s = getState(root);
        if (!s) return;
        if (s.nativeScrollSyncTimer) clearTimeout(s.nativeScrollSyncTimer);
        s.nativeScrollSyncTimer = setTimeout(() => {
            s.nativeScrollSyncTimer = 0;
            syncNativeScrollFromLogical(root);
            requestViewportSync(root, true);
        }, delay ?? 90);
    }

    function markRendererDirty(root, kind, reason) {
        const renderer = getState(root)?.renderer;
        if (!renderer) return;
        const dirty = renderer.dirty;
        const next = kind || "full";
        if (next === "selection") {
            dirty.selection = true;
            renderer.layers.selection.dirty = true;
        } else if (next === "headers") {
            dirty.header = true;
            dirty.selection = true;
            renderer.layers.header.dirty = true;
            renderer.layers.selection.dirty = true;
        } else if (next === "content") {
            dirty.content = true;
            dirty.selection = true;
            renderer.layers.content.dirty = true;
            renderer.layers.selection.dirty = true;
        } else {
            dirty.full = true;
            dirty.content = true;
            dirty.header = true;
            dirty.selection = true;
            dirty.editor = true;
            renderer.layers.content.dirty = true;
            renderer.layers.header.dirty = true;
            renderer.layers.selection.dirty = true;
            renderer.layers.editor.dirty = true;
        }
        renderer.lastDirtyReason = reason || "";
    }

    function consumeRendererDirty(root) {
        const renderer = getState(root)?.renderer;
        if (!renderer) {
            return {
                content: true,
                header: true,
                selection: true,
                editor: true,
            full: true,
            contentRects: [],
            headerRects: [],
            selectionRects: []
        };
        }

        const snapshot = {
            ...renderer.dirty,
            contentRects: renderer.dirtyRects.content.splice(0),
            headerRects: renderer.dirtyRects.header.splice(0),
            selectionRects: renderer.dirtyRects.selection.splice(0)
        };
        renderer.dirty = { content: false, header: false, selection: false, editor: false, full: false };
        for (const layer of Object.values(renderer.layers)) {
            layer.dirty = false;
        }
        return snapshot;
    }

    /** Paints an opaque token colour at an alpha without leaking the globalAlpha into the next draw. */
    function fillRectWithAlpha(ctx, color, alpha, x, y, w, h, metrics) {
        const weight = Number(alpha);
        if (!color || !Number.isFinite(weight) || weight >= 1 || weight <= 0) {
            setContextFillStyle(ctx, color, metrics);
            ctx.fillRect(x, y, w, h);
            return;
        }

        const previous = ctx.globalAlpha;
        ctx.globalAlpha = previous * weight;
        setContextFillStyle(ctx, color, metrics);
        ctx.fillRect(x, y, w, h);
        ctx.globalAlpha = previous;
    }

    function addDirtyRect(root, layer, rect) {
        const s = getState(root);
        const renderer = s?.renderer;
        if (!renderer || !rect || rect.width <= 0 || rect.height <= 0) return;
        const expanded = {
            x: Math.max(0, Math.floor(rect.x) - 3),
            y: Math.max(0, Math.floor(rect.y) - 3),
            width: Math.ceil(rect.width) + 6,
            height: Math.ceil(rect.height) + 6
        };
        renderer.dirtyRects[layer]?.push(expanded);
        if (s.metrics) {
            if (layer === "selection") {
                s.metrics.selectionDirtyRectCount += 1;
                s.metrics.selectionDirtyRectArea += expanded.width * expanded.height;
            } else if (layer === "content") {
                s.metrics.contentDirtyRectCount += 1;
                s.metrics.contentDirtyRectArea += expanded.width * expanded.height;
            }
        }
    }

    /** Clamps every dirty rect to the surface and drops empty ones (the header layer clips each one). */
    function normalizeDirtyRects(rects, width, height) {
        const normalized = [];
        for (const rect of rects || []) {
            const x = Math.max(0, Math.floor(rect.x));
            const y = Math.max(0, Math.floor(rect.y));
            const right = Math.min(width, Math.ceil(rect.x + rect.width));
            const bottom = Math.min(height, Math.ceil(rect.y + rect.height));
            if (right > x && bottom > y) {
                normalized.push({ x, y, width: right - x, height: bottom - y });
            }
        }
        return normalized;
    }

    function unionRects(rects, width, height) {
        if (!Array.isArray(rects) || rects.length === 0) return null;
        let left = Infinity;
        let top = Infinity;
        let right = -Infinity;
        let bottom = -Infinity;
        for (const rect of rects) {
            left = Math.min(left, rect.x);
            top = Math.min(top, rect.y);
            right = Math.max(right, rect.x + rect.width);
            bottom = Math.max(bottom, rect.y + rect.height);
        }
        if (!Number.isFinite(left) || !Number.isFinite(top) || right <= left || bottom <= top) return null;
        left = Math.max(0, left);
        top = Math.max(0, top);
        right = Math.min(width, right);
        bottom = Math.min(height, bottom);
        return right > left && bottom > top
            ? { x: left, y: top, width: right - left, height: bottom - top }
            : null;
    }

    function addResizeCommitDirtyRects(root, kind, index) {
        const s = getState(root);
        const model = s?.model;
        if (!s || !model) return;

        syncModelViewport(root, model);
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const layout = getVisibleLayout(root, model, width, height);

        if (kind === "column") {
            const colFrame = getFrameByIndex(layout.columns, index);
            const x = colFrame
                ? Math.max(rowHeaderWidth, Math.floor(read(colFrame, "x", rowHeaderWidth)))
                : rowHeaderWidth;
            const stripWidth = Math.max(1, width - x);
            addDirtyRect(root, "content", { x, y: columnHeaderHeight, width: stripWidth, height: Math.max(1, height - columnHeaderHeight) });
            addDirtyRect(root, "header", { x, y: 0, width: stripWidth, height: columnHeaderHeight });
        } else {
            const rowFrame = getFrameByIndex(layout.rows, index);
            const y = rowFrame
                ? Math.max(columnHeaderHeight, Math.floor(read(rowFrame, "y", columnHeaderHeight)))
                : columnHeaderHeight;
            const stripHeight = Math.max(1, height - y);
            addDirtyRect(root, "content", { x: rowHeaderWidth, y, width: Math.max(1, width - rowHeaderWidth), height: stripHeight });
            addDirtyRect(root, "header", { x: 0, y, width: rowHeaderWidth, height: stripHeight });
        }

        addDirtyRect(root, "selection", { x: 0, y: 0, width, height });
    }

    function dirtyRank(kind) {
        return ({ selection: 1, headers: 2, content: 3, full: 4 })[kind] || 4;
    }

    function mergeRedrawKind(current, next) {
        if (!current) return next || "full";
        return dirtyRank(next) > dirtyRank(current) ? next : current;
    }

    function requestPaint(root, reason, kind) {
        const s = getState(root);
        if (!s || !s.model) return;
        const nextKind = kind || "full";
        markRendererDirty(root, nextKind, reason);
        const previousKind = s.dirtyKind || "";
        const mergedKind = mergeRedrawKind(previousKind, nextKind);
        const isMergedRequest = !!s.paintRequested;

        if (s.metrics) {
            s.metrics.paintRequestCount += 1;
            if (isMergedRequest) {
                s.metrics.mergedPaintRequestCount += 1;
                s.metrics.discardedIntermediatePaintCount += 1;
            }
        }

        s.pendingPaintRequestCount = (s.pendingPaintRequestCount || 0) + 1;
        s.dirtyKind = mergedKind;
        if (!previousKind || dirtyRank(nextKind) >= dirtyRank(previousKind)) {
            s.dirtyReason = reason || "unknown";
        } else if (!s.dirtyReason) {
            s.dirtyReason = reason || "unknown";
        }

        if (s.paintRequested) return;
        s.paintRequested = true;
        s.paintFrame = requestAnimationFrame(() => {
            const redrawReason = s.dirtyReason || "unknown";
            const redrawKind = s.dirtyKind || "full";
            const oldScrollLeft = Number.isFinite(s.paintedScrollLeft) ? s.paintedScrollLeft : read(s.model, "ScrollLeft", getLogicalScrollLeft(root));
            const oldScrollTop = Number.isFinite(s.paintedScrollTop) ? s.paintedScrollTop : read(s.model, "ScrollTop", getLogicalScrollTop(root));
            const requestCount = s.pendingPaintRequestCount || 1;
            s.paintFrame = 0;
            s.paintRequested = false;
            s.pendingPaintRequestCount = 0;
            s.dirtyReason = "";
            s.dirtyKind = "";
            const dirty = consumeRendererDirty(root);
            if (s.metrics) {
                s.metrics.paintFrameCount += 1;
                s.metrics.maxMergedPaintRequestsPerFrame = Math.max(s.metrics.maxMergedPaintRequestsPerFrame || 0, requestCount);
                s.metrics.lastRedrawSource = redrawReason;
                s.metrics.lastRedrawKind = redrawKind;
            }
            if (redrawKind === "selection") {
                syncModelViewport(root, s.model);
                if (dirty.header || dirty.headerRects.length > 0) {
                    // A selection change moves the active-row/column tint: repaint ONLY the old and the
                    // new header strips (the dirty rects addSelectionDirtyRectForChange registered), never
                    // the whole header layer.
                    renderHeaderLayer(root, s.model, { dirtyRects: dirty.headerRects, forceFull: dirty.header });
                }
                renderSelectionOverlay(root, s.model, { dirtyRects: dirty.selectionRects });
                if (s.metrics) s.metrics.selectionPaintFrameCount += 1;
            } else if (redrawReason === "scroll" && tryBitmapScrollRedraw(root, s.model, oldScrollLeft, oldScrollTop)) {
                s.paintedScrollLeft = getLogicalScrollLeft(root);
                s.paintedScrollTop = getLogicalScrollTop(root);
                if (s.metrics) s.metrics.contentPaintFrameCount += 1;
                return;
            } else if (redrawKind === "content" && !dirty.full) {
                syncModelViewport(root, s.model);
                const metrics = renderContentLayer(root, s.model, { dirtyRects: dirty.contentRects });
                if (dirty.header || dirty.headerRects.length > 0) renderHeaderLayer(root, s.model, { dirtyRects: dirty.headerRects });
                if (dirty.selection) renderSelectionOverlay(root, s.model, { dirtyRects: dirty.selectionRects });
                s.paintedScrollLeft = getLogicalScrollLeft(root);
                s.paintedScrollTop = getLogicalScrollTop(root);
                if (s.metrics) {
                    s.metrics.contentPaintFrameCount += 1;
                    s.metrics.lastTextDrawMs = metrics.textMs;
                    s.metrics.lastVisibleCellCount = metrics.cells;
                    s.metrics.lastTextCount = metrics.texts;
                }
            } else if (redrawKind === "headers" && !dirty.full) {
                syncModelViewport(root, s.model);
                renderHeaderLayer(root, s.model, { dirtyRects: dirty.headerRects });
                if (dirty.selection) renderSelectionOverlay(root, s.model, { dirtyRects: dirty.selectionRects });
            } else {
                syncModelViewport(root, s.model);
                renderModel(root, s.canvas, s.model);
                s.paintedScrollLeft = getLogicalScrollLeft(root);
                s.paintedScrollTop = getLogicalScrollTop(root);
                if (s.metrics) s.metrics.contentPaintFrameCount += 1;
            }
        });
    }

    function requestCanvasRedraw(root, reason, kind) {
        requestPaint(root, reason, kind);
    }

    function scheduleLocalRender(root) {
        requestPaint(root, "scroll", "full");
    }

    function markLocalInteraction(root, source) {
        const s = getState(root);
        if (!s) return 0;
        const revision = bumpLocalRevision(root, source || "local");
        if (s.metrics) {
            if (source === "keyboard") s.metrics.keyboardInteractions += 1;
            else if (source === "scroll") s.metrics.scrollInteractions += 1;
            else if (source === "pointer") s.metrics.pointerInteractions += 1;
        }
        s.lastLocalInteractionAt = performance.now();
        return revision;
    }

    function currentInteractionVersion(root) {
        const s = getState(root);
        return Math.max(s?.interactionVersion || 0, s?.sheetState?.localRevision || 0);
    }

    function recordDotNetCallback(root, method, hotPath) {
        const s = getState(root);
        const metrics = s?.metrics;
        if (!metrics) return;
        const key = method || "unknown";
        metrics.dotNetCallbackCount += 1;
        metrics.lastDotNetCallbackMethod = key;
        metrics.dotNetCallbacksByMethod[key] = (metrics.dotNetCallbacksByMethod[key] || 0) + 1;
        const age = performance.now() - (s?.lastLocalInteractionAt || 0);
        if (s?.lastInteractionSource === "resize" && age >= 0 && age < 1000) {
            metrics.resizeDotNetCallbackCount += 1;
        }
        if (hotPath) {
            metrics.hotPathDotNetCallbackCount += 1;
            metrics.hotPathDotNetCallbacksByMethod[key] = (metrics.hotPathDotNetCallbacksByMethod[key] || 0) + 1;
        }
    }

    function invokeDotNet(root, method, args, hotPath) {
        const s = getState(root);
        if (!s?.dotNet) return Promise.resolve();
        const isHotPath = hotPath == null
            ? performance.now() - (s.lastLocalInteractionAt || 0) < 500
            : !!hotPath;
        recordDotNetCallback(root, method, isHotPath);
        return s.dotNet.invokeMethodAsync(method, ...(args || []));
    }

    function countCommandType(metrics, type) {
        if (!metrics) return;
        if (type === "cellChanged") metrics.cellChangedCommandCount += 1;
        else if (type === "rangeChanged") metrics.rangeChangedCommandCount += 1;
        else if (type === "selectionSettled") metrics.selectionSettledCommandCount += 1;
        else if (type === "viewportSettled") metrics.viewportSettledCommandCount += 1;
        else if (type === "formulaCommitted") metrics.formulaCommittedCommandCount += 1;
        else if (type === "columnResized") metrics.columnResizeCommandCount += 1;
        else if (type === "rowResized") metrics.rowResizeCommandCount += 1;
    }

    function queueCommand(root, type, payload, options) {
        const s = getState(root);
        if (!s) return null;
        const command = {
            id: ++s.commandLogSeq,
            type,
            event: type,
            interactionVersion: currentInteractionVersion(root),
            createdAt: performance.now(),
            attempts: 0,
            ...(payload || {})
        };

        if (command.id <= (s.commandLogAckRevision || 0)) {
            if (s.metrics) s.metrics.commandLogObsoleteDropCount += 1;
            return null;
        }

        s.commandLog.push(command);
        if (s.metrics) {
            s.metrics.commandLogQueuedCount += 1;
            countCommandType(s.metrics, type);
        }

        const delay = options?.delay ?? defaultCommandLogDebounceMs;
        if (options?.flushNow) {
            flushCommandLog(root);
        } else {
            scheduleCommandLogFlush(root, delay);
        }
        return command;
    }

    function scheduleCommandLogFlush(root, delay) {
        const s = getState(root);
        if (!s) return;
        if (s.commandLogTimer) clearTimeout(s.commandLogTimer);
        s.commandLogTimer = setTimeout(() => flushCommandLog(root), delay ?? defaultCommandLogDebounceMs);
    }

    // The highest command id a flush may send right now. A .NET key command waiting in
    // invokeDotNetAfterPendingState registers the last id queued BEFORE its key press as a barrier; no
    // flush — timer-driven or not — may carry a later command across it, or .NET would run the key
    // command against state from after the press. A barrier holds until its key command has been
    // DISPATCHED (not merely until .NET acked the commands before it): between that ack and the dispatch
    // the key command may still be queued behind an earlier one.
    function commandLogFlushLimit(s, uptoSeq) {
        let limit = uptoSeq == null ? Infinity : uptoSeq;
        for (const barrier of s.commandLogBarriers || []) {
            if (barrier.target < limit) limit = barrier.target;
        }
        return limit;
    }

    function flushCommandLog(root, uptoSeq) {
        const s = getState(root);
        if (!s || !s.dotNet) return;
        if (s.commandLogTimer) {
            clearTimeout(s.commandLogTimer);
            s.commandLogTimer = 0;
        }
        if (s.commandLogInFlight) {
            s.commandLogPending = true;
            return;
        }

        const ack = s.commandLogAckRevision || 0;
        const limit = commandLogFlushLimit(s, uptoSeq);
        const live = (s.commandLog || []).filter(command => command.id > ack);
        const obsoleteCount = (s.commandLog || []).length - live.length;
        if (obsoleteCount > 0 && s.metrics) s.metrics.commandLogObsoleteDropCount += obsoleteCount;
        const batch = live.filter(command => command.id <= limit);
        // Commands past the limit stay queued; whoever lifts the barrier reschedules them.
        s.commandLog = live.filter(command => command.id > limit);
        if (batch.length === 0) return;

        s.commandLogInFlight = true;
        if (s.metrics) {
            s.metrics.commandLogBatchCallbackCount += 1;
            s.metrics.commandLogBatchItemCount += batch.length;
        }

        s.commandLogFlight = invokeDotNet(root, "OnCanvasCommandLogBatch", [batch], false)
            .then(ackRevision => {
                const nextAck = Number(ackRevision) || Math.max(...batch.map(command => command.id));
                s.commandLogAckRevision = Math.max(s.commandLogAckRevision || 0, nextAck);
                if (s.metrics) s.metrics.commandLogAckRevision = s.commandLogAckRevision;
            })
            .catch(() => {
                const retry = [];
                for (const command of batch) {
                    command.attempts = (command.attempts || 0) + 1;
                    if (command.attempts <= 2 && command.id > (s.commandLogAckRevision || 0)) {
                        retry.push(command);
                    } else if (s.metrics) {
                        s.metrics.commandLogObsoleteDropCount += 1;
                    }
                }
                if (retry.length > 0) {
                    s.commandLog.unshift(...retry);
                    if (s.metrics) s.metrics.commandLogRetryCount += retry.length;
                    s.commandLogPending = true;
                }
            })
            .finally(() => {
                s.commandLogInFlight = false;
                if (s.commandLogPending || s.commandLog.length > 0) {
                    s.commandLogPending = false;
                    scheduleCommandLogFlush(root, 45);
                }
            });
    }

    function scheduleForcedViewportSync(root) {
        requestViewportSync(root, true);
    }

    function requestViewportSync(root, force) {
        const s = getState(root);
        if (!s) return;
        s.syncRequested = true;
        s.syncForce = s.syncForce || !!force;
        if (s.metrics) s.metrics.syncRequestCount += 1;
        if (s.syncFrame) return;
        s.syncFrame = requestAnimationFrame(() => {
            const syncForce = !!s.syncForce;
            s.syncFrame = 0;
            s.syncRequested = false;
            s.syncForce = false;
            sendViewport(root, syncForce);
        });
    }

    function sendViewport(root, force) {
        const s = getState(root);
        if (!s) return;
        if (s.viewportTimer) {
            clearTimeout(s.viewportTimer);
            s.viewportTimer = 0;
        }
        const scrollLeft = getLogicalScrollLeft(root);
        const scrollTop = getLogicalScrollTop(root);
        s.syncedScrollLeft = scrollLeft;
        s.syncedScrollTop = scrollTop;
        s.lastViewportSync = performance.now();
        const selection = getSelectionSnapshot(root);
        if (s.metrics) s.metrics.viewportCallbackCount += 1;
        const delay = force
            ? 0
            : getSettledCommandDelay(root, defaultCommandLogDebounceMs);

        queueCommand(root, "viewportSettled", {
            scrollLeft,
            scrollTop,
            clientWidth: root.clientWidth || 0,
            clientHeight: root.clientHeight || 0,
            selection,
            force: !!force
        }, { delay, flushNow: !!force });
    }

    function notifyViewport(root, force) {
        const s = getState(root);
        if (!s) return;

        const now = performance.now();
        const movedX = Math.abs(getLogicalScrollLeft(root) - (s.syncedScrollLeft || 0));
        const movedY = Math.abs(getLogicalScrollTop(root) - (s.syncedScrollTop || 0));
        const shouldSyncNow = force
            || movedX > Math.max(160, (root.clientWidth || 0) * 0.35)
            || movedY > Math.max(120, (root.clientHeight || 0) * 0.35)
            || now - (s.lastViewportSync || 0) > 140;

        if (shouldSyncNow) {
            requestViewportSync(root, !!force);
            return;
        }

        if (!s.viewportTimer) {
            s.viewportTimer = setTimeout(() => sendViewport(root, false), 120);
        }
    }

    function debounceViewportSyncAfterPaint(root, force, delay) {
        const s = getState(root);
        if (!s) return;
        s.postPaintDebouncedViewportForce = s.postPaintDebouncedViewportForce || !!force;
        if (s.postPaintDebouncedViewportFrame) return;
        s.postPaintDebouncedViewportFrame = requestAnimationFrame(() => {
            const nextForce = !!s.postPaintDebouncedViewportForce;
            s.postPaintDebouncedViewportFrame = 0;
            s.postPaintDebouncedViewportForce = false;

            if (nextForce) {
                requestViewportSync(root, true);
                return;
            }

            if (s.viewportTimer) clearTimeout(s.viewportTimer);
            s.viewportTimer = setTimeout(() => sendViewport(root, false), delay ?? 120);
        });
    }

    function normalizeWheelDelta(ev, root) {
        const lineSize = 32;
        const pageHeight = Math.max(1, root.clientHeight || 1);
        const pageWidth = Math.max(1, root.clientWidth || 1);
        let dx = Number(ev.deltaX) || 0;
        let dy = Number(ev.deltaY) || 0;

        if (ev.deltaMode === WheelEvent.DOM_DELTA_LINE) {
            dx *= lineSize;
            dy *= lineSize;
        } else if (ev.deltaMode === WheelEvent.DOM_DELTA_PAGE) {
            dx *= pageWidth;
            dy *= pageHeight;
        }

        if (ev.shiftKey && !dx && dy) {
            dx = dy;
            dy = 0;
        }

        return { dx, dy };
    }

    function getSelectionSnapshot(root) {
        const sheet = getSheetState(root);
        if (sheet) {
            return {
                row: sheet.activeCell.row,
                col: sheet.activeCell.col,
                startRow: sheet.selection.startRow,
                startCol: sheet.selection.startCol,
                endRow: sheet.selection.endRow,
                endCol: sheet.selection.endCol
            };
        }

        const model = getState(root)?.model;
        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        const selection = read(model, "Selection", {});
        return {
            row: active.row,
            col: active.col,
            startRow: read(selection, "StartRow", active.row),
            startCol: read(selection, "StartCol", active.col),
            endRow: read(selection, "EndRow", active.row),
            endCol: read(selection, "EndCol", active.col)
        };
    }

    function applySelectionSnapshot(model, snapshot) {
        if (!model || !snapshot) return;
        const selection = read(model, "Selection", {});
        const activeRef = toCellRef(snapshot.row, snapshot.col);
        const startRow = Number(snapshot.startRow ?? snapshot.row) || 0;
        const startCol = Number(snapshot.startCol ?? snapshot.col) || 0;
        const endRow = Number(snapshot.endRow ?? snapshot.row) || 0;
        const endCol = Number(snapshot.endCol ?? snapshot.col) || 0;
        const minRow = Math.min(startRow, endRow);
        const maxRow = Math.max(startRow, endRow);
        const minCol = Math.min(startCol, endCol);
        const maxCol = Math.max(startCol, endCol);

        write(model, "ActiveCellRef", activeRef);
        write(selection, "StartRow", startRow);
        write(selection, "StartCol", startCol);
        write(selection, "EndRow", endRow);
        write(selection, "EndCol", endCol);
        write(model, "Selection", selection);

        for (const cell of read(model, "Cells", [])) {
            const cellRow = read(cell, "Row", -1);
            const cellCol = read(cell, "Col", -1);
            const selected = cellRow >= minRow && cellRow <= maxRow && cellCol >= minCol && cellCol <= maxCol;
            write(cell, "Active", cellRow === snapshot.row && cellCol === snapshot.col);
            write(cell, "Selected", selected);
            write(cell, "SelectionEnd", cellRow === endRow && cellCol === endCol);
        }
    }

    function preserveLocalInteraction(root, model) {
        const s = getState(root);
        if (!s || !s.model || !model) return model;

        const frameVersion = Number(read(model, "InteractionVersion", 0)) || 0;
        const localRevision = Math.max(s.interactionVersion || 0, s.sheetState?.localRevision || 0);
        if (frameVersion >= localRevision) return model;

        if (s.metrics) s.metrics.staleFramesIgnored += 1;
        write(model, "ScrollLeft", getLogicalScrollLeft(root));
        write(model, "ScrollTop", getLogicalScrollTop(root));
        applySelectionSnapshot(model, getSelectionSnapshot(root));
        if (s.sheetState) {
            write(model, "IsFormulaPointMode", s.sheetState.formulaMode);
            write(model, "IsFormatPainterActive", s.sheetState.formatPainterActive);
        }
        write(model, "InteractionVersion", localRevision);
        return model;
    }

    function syncSheetStateFromModel(root, model, serverRevisionOverride) {
        const s = getState(root);
        const sheet = s?.sheetState;
        if (!s || !sheet || !model) return;

        const active = parseCellRef(read(model, "ActiveCellRef", sheet.activeCell.ref || "A1"));
        const selection = read(model, "Selection", {});
        const startRow = Number(read(selection, "StartRow", active.row)) || 0;
        const startCol = Number(read(selection, "StartCol", active.col)) || 0;
        const endRow = Number(read(selection, "EndRow", active.row)) || 0;
        const endCol = Number(read(selection, "EndCol", active.col)) || 0;
        setSheetSelection(root, active.row, active.col, startRow, startCol, endRow, endCol);

        const scrollLeft = Number(read(model, "ScrollLeft", getLogicalScrollLeft(root))) || 0;
        const scrollTop = Number(read(model, "ScrollTop", getLogicalScrollTop(root))) || 0;
        s.logicalScrollLeft = scrollLeft;
        s.logicalScrollTop = scrollTop;
        setSheetScroll(root, scrollLeft, scrollTop);

        sheet.formulaMode = !!read(model, "IsFormulaPointMode", sheet.formulaMode);
        sheet.formatPainterActive = !!read(model, "IsFormatPainterActive", sheet.formatPainterActive);

        const serverRevision = Number(serverRevisionOverride ?? read(model, "InteractionVersion", sheet.serverRevision || 0)) || 0;
        s.blazorRevisionCounter = Math.max((s.blazorRevisionCounter || 0) + 1, serverRevision);
        sheet.serverRevision = serverRevision;
        sheet.blazorRevision = s.blazorRevisionCounter;
        sheet.lastBlazorRevisionAt = performance.now();
        if (s.workbookState) {
            s.workbookState.serverRevision = serverRevision;
            s.workbookState.blazorRevision = s.blazorRevisionCounter;
        }
    }

    /** A token, or the second token when the first is not defined for this host. Never a literal. */
    function cssToken(root, name, fallbackName) {
        const value = css(root, name, "");
        if (value) return value;
        return fallbackName ? css(root, fallbackName, "") : "";
    }

    /**
     * Splits a comma separated argument list at the TOP level (a `color(srgb …)` or a nested
     * `color-mix()` keeps its own commas). Used by the colour-mix parser only.
     */
    function splitTopLevel(value) {
        const parts = [];
        let depth = 0;
        let current = "";
        for (const character of String(value)) {
            if (character === "(") depth += 1;
            if (character === ")") depth -= 1;
            if (character === "," && depth === 0) {
                parts.push(current.trim());
                current = "";
                continue;
            }
            current += character;
        }
        if (current.trim()) parts.push(current.trim());
        return parts;
    }

    /**
     * Parses the colour notations a COMPUTED component token can carry into `{ r, g, b, a }`:
     * `#rgb`/`#rrggbb`/`#rrggbbaa`, `rgb()`/`rgba()`, `color(srgb r g b / a)` (what Chromium
     * serialises a resolved `color-mix()` to) and `color-mix(in srgb, A p%, B)` (what an
     * UNREGISTERED custom property keeps — custom properties substitute var() but never compute a
     * colour function). `var(--x)` operands are resolved through `root` when a host hands one over.
     * Anything else (a relative colour, `currentColor`) returns null so the caller keeps the raw
     * token instead of painting garbage.
     */
    function parseColor(value, root) {
        const text = String(value || "").trim();
        if (!text) return null;

        const varCall = /^var\(\s*(--[a-z0-9-]+)\s*(?:,[\s\S]*)?\)$/i.exec(text);
        if (varCall) {
            const resolved = root ? css(root, varCall[1], "") : "";
            return resolved ? parseColor(resolved, root) : null;
        }

        const hex = /^#([0-9a-f]{3,8})$/i.exec(text);
        if (hex) {
            let digits = hex[1];
            if (digits.length === 3 || digits.length === 4) {
                digits = digits.split("").map(ch => ch + ch).join("");
            }
            if (digits.length !== 6 && digits.length !== 8) return null;
            return {
                r: parseInt(digits.slice(0, 2), 16),
                g: parseInt(digits.slice(2, 4), 16),
                b: parseInt(digits.slice(4, 6), 16),
                a: digits.length === 8 ? parseInt(digits.slice(6, 8), 16) / 255 : 1
            };
        }

        const rgb = /^rgba?\(([\s\S]*)\)$/i.exec(text);
        if (rgb) {
            const body = rgb[1].trim();
            // A relative colour (`rgb(from …)`) is not a plain channel list — leave it to the browser.
            if (/^from\b/i.test(body)) return null;
            const [channels, alphaPart] = body.split("/");
            const numbers = channels.split(/[,\s]+/).filter(Boolean);
            if (numbers.length < 3 || numbers.some(part => !/^[\d.]+%?$/.test(part))) return null;
            const channel = part => Math.round(part.endsWith("%")
                ? (Number(part.slice(0, -1)) / 100) * 255
                : Number(part));
            let alpha = 1;
            if (alphaPart != null) {
                const trimmed = alphaPart.trim();
                if (!/^[\d.]+%?$/.test(trimmed)) return null;
                alpha = trimmed.endsWith("%") ? Number(trimmed.slice(0, -1)) / 100 : Number(trimmed);
            } else if (numbers.length >= 4) {
                const last = numbers[3];
                alpha = last.endsWith("%") ? Number(last.slice(0, -1)) / 100 : Number(last);
            }
            return { r: channel(numbers[0]), g: channel(numbers[1]), b: channel(numbers[2]), a: alpha };
        }

        const color = /^color\(([\s\S]*)\)$/i.exec(text);
        if (color) {
            const body = color[1].trim();
            const space = /^([a-z0-9-]+)\s+([\s\S]+)$/i.exec(body);
            if (!space || space[1].toLowerCase() !== "srgb") return null;
            const [channels, alphaPart] = space[2].split("/");
            const numbers = channels.trim().split(/\s+/).filter(Boolean);
            if (numbers.length < 3) return null;
            const unit = part => {
                if (part.endsWith("%")) return (Number(part.slice(0, -1)) / 100) * 255;
                return Number(part) * 255;
            };
            const alpha = alphaPart == null
                ? 1
                : (alphaPart.trim().endsWith("%")
                    ? Number(alphaPart.trim().slice(0, -1)) / 100
                    : Number(alphaPart.trim()));
            const parsed = { r: unit(numbers[0]), g: unit(numbers[1]), b: unit(numbers[2]), a: alpha };
            // `none` and any other non-numeric component must NOT leak NaN into fillStyle — fall back.
            return isFiniteColor(parsed) ? parsed : null;
        }

        const mix = /^color-mix\(([\s\S]*)\)$/i.exec(text);
        if (mix) {
            const parts = splitTopLevel(mix[1]);
            if (parts.length < 3) return null;
            const space = /^in\s+([a-z0-9-]+)$/i.exec(parts[0]);
            if (!space) return null;
            if (space[1].toLowerCase() !== "srgb") return null;
            const operands = parts.slice(1).map(part => {
                const percent = /\s([\d.]+)%\s*$/.exec(part);
                return {
                    color: parseColor(percent ? part.slice(0, percent.index).trim() : part, root),
                    weight: percent ? Number(percent[1]) / 100 : null
                };
            });
            if (operands.length !== 2 || !operands[0].color || !operands[1].color) return null;
            const first = operands[0].weight ?? (operands[1].weight == null ? 0.5 : 1 - operands[1].weight);
            const second = operands[1].weight ?? 1 - first;
            const total = first + second || 1;
            // Conservative contract: the canvas only MIXES two OPAQUE colours that add up to a full
            // 100 %. A translucent operand or a short weight sum (< 100 %) would produce something the
            // mixer cannot reason about, so the caller keeps the RAW token and the browser resolves it.
            if (operands[0].color.a < 1 || operands[1].color.a < 1) return null;
            if (total < 1 - 1e-6) return null;
            const mixChannel = name =>
                (operands[0].color[name] * first + operands[1].color[name] * second) / total;
            const parsed = {
                r: mixChannel("r"),
                g: mixChannel("g"),
                b: mixChannel("b"),
                a: mixChannel("a")
            };
            return isFiniteColor(parsed) ? parsed : null;
        }

        return null;
    }

    /** True when every channel and the alpha of a parsed colour are finite numbers (no NaN from `none`). */
    function isFiniteColor(parsed) {
        return !!parsed
            && Number.isFinite(parsed.r)
            && Number.isFinite(parsed.g)
            && Number.isFinite(parsed.b)
            && Number.isFinite(parsed.a);
    }

    /** The canonical `rgb()`/`rgba()` string of a parsed colour (alpha 1 is never emitted as rgba). */
    function formatColor(parsed) {
        if (!parsed) return "";
        const channel = value => Math.max(0, Math.min(255, Math.round(Number(value) || 0)));
        const alpha = Math.max(0, Math.min(1, Number(parsed.a)));
        if (alpha >= 1) return `rgb(${channel(parsed.r)}, ${channel(parsed.g)}, ${channel(parsed.b)})`;
        return `rgba(${channel(parsed.r)}, ${channel(parsed.g)}, ${channel(parsed.b)}, ${Math.round(alpha * 1000) / 1000})`;
    }

    /**
     * A token normalised for the canvas: the parsed colour when the notation is understood (so a
     * `color-mix()` token is mixed HERE instead of being handed to `fillStyle`, where a browser that
     * does not resolve it would silently keep the previous style), otherwise the raw token.
     */
    function resolveColorToken(root, value) {
        const raw = String(value || "").trim();
        if (!raw) return { value: raw, alpha: null };
        const parsed = parseColor(raw, root);
        if (!parsed) return { value: raw, alpha: null };
        return { value: formatColor(parsed), alpha: Math.max(0, Math.min(1, Number(parsed.a))) };
    }

    /**
     * A translucent rgba() built from an OPAQUE token colour. Only hex and rgb()/rgba() tokens are
     * parsed; any other notation (a relative-colour or color-mix() token) is returned unchanged so the
     * browser keeps resolving it rather than the canvas receiving garbage.
     */
    function withAlpha(color, alpha) {
        const value = String(color || "").trim();
        const weight = Number(alpha);
        if (!value || !Number.isFinite(weight) || weight >= 1) return value;

        const hex = /^#([0-9a-f]{3,8})$/i.exec(value);
        if (hex) {
            let digits = hex[1];
            if (digits.length === 3 || digits.length === 4) {
                digits = digits.split("").map(ch => ch + ch).join("");
            }
            if (digits.length !== 6 && digits.length !== 8) return value;
            const r = parseInt(digits.slice(0, 2), 16);
            const g = parseInt(digits.slice(2, 4), 16);
            const b = parseInt(digits.slice(4, 6), 16);
            return `rgba(${r}, ${g}, ${b}, ${weight})`;
        }

        const rgb = /^rgba?\(\s*([0-9.]+)[,\s]+([0-9.]+)[,\s]+([0-9.]+)/i.exec(value);
        if (rgb) {
            const round = part => Math.round(Number(part) || 0);
            return `rgba(${round(rgb[1])}, ${round(rgb[2])}, ${round(rgb[3])}, ${weight})`;
        }

        return value;
    }

    /**
     * Every colour the canvas paints. Tokens only: the component tokens declared on .tm-spreadsheet,
     * each degrading to the core semantic token it derives from. Nothing here is a literal, so light,
     * dark and the indigo theme reach the canvas (and the node lane can assert it).
     */
    function buildPalette(root) {
        const primary = css(root, "--tm-color-primary", "");
        const warning = cssToken(root, "--tm-color-warning", "--tm-color-warning-strong");
        const categoryRefs = ["--tm-category-1", "--tm-category-6", "--tm-category-4",
            "--tm-category-2", "--tm-category-3", "--tm-category-8"];
        const formulaRefs = categoryRefs.map((fallbackToken, index) => {
            const stroke = cssToken(root, `--tm-spreadsheet-ref-${index}`, fallbackToken);
            return { stroke, fill: withAlpha(stroke, 0.08) };
        });
        const fillAlpha = Number(css(root, "--tm-spreadsheet-selection-fill-alpha", ""));
        // The active header strip: a `color-mix()` token (the OPAQUE dark override) is mixed here,
        // because an unregistered custom property keeps the function unevaluated and a browser that
        // refuses it in fillStyle would leave the previous style in place. `alpha` is null whenever the
        // notation is not understood, so a caller can never claim opacity it does not have.
        const headerActive = resolveColorToken(
            root, cssToken(root, "--tm-spreadsheet-header-active-bg", "--tm-color-primary-subtle"));

        return {
            surface: css(root, "--tm-color-surface", ""),
            elevated: css(root, "--tm-color-surface-elevated", ""),
            hover: css(root, "--tm-color-surface-hover", ""),
            border: css(root, "--tm-color-border", ""),
            subtle: cssToken(root, "--tm-color-border-subtle", "--tm-color-border"),
            text: cssToken(root, "--tm-color-text", "--tm-text-primary"),
            muted: cssToken(root, "--tm-color-text-muted", "--tm-text-secondary"),
            primary,
            primarySubtle: cssToken(root, "--tm-color-primary-subtle", "--tm-color-primary-soft"),
            headerBg: cssToken(root, "--tm-spreadsheet-header-bg", "--tm-color-surface-elevated"),
            headerText: cssToken(root, "--tm-spreadsheet-header-text", "--tm-color-text-muted"),
            headerActiveBg: headerActive.value,
            headerActiveBgAlpha: headerActive.alpha,
            headerActiveText: cssToken(root, "--tm-spreadsheet-header-active-text", "--tm-color-primary-text"),
            headerFont: css(root, "--tm-spreadsheet-header-font", defaultHeaderFont),
            gridline: cssToken(root, "--tm-spreadsheet-gridline", "--tm-color-border-subtle"),
            selectionBorder: cssToken(root, "--tm-spreadsheet-selection-border", "--tm-color-primary"),
            selectionFill: cssToken(root, "--tm-spreadsheet-selection-fill", "--tm-color-primary"),
            selectionFillAlpha: Number.isFinite(fillAlpha) && fillAlpha > 0 && fillAlpha <= 1
                ? fillAlpha
                : selectionFillAlphaDefault,
            hoverFill: withAlpha(cssToken(root, "--tm-color-gray-400", "--tm-color-text-muted"), 0.12),
            danger: cssToken(root, "--tm-color-danger", "--tm-color-error"),
            warning,
            inverseText: cssToken(root, "--tm-color-on-primary", "--tm-text-inverse"),
            hyperlink: cssToken(root, "--tm-text-link", "--tm-color-primary"),
            searchMatchFill: withAlpha(warning, 0.3),
            searchActiveFill: withAlpha(warning, 0.45),
            searchActiveStroke: cssToken(root, "--tm-color-warning-strong", "--tm-color-warning"),
            formulaRefs
        };
    }

    /** A pointer media query cannot be answered without the browser; a host without matchMedia is fine. */
    function isCoarsePointer() {
        try {
            return !!(window.matchMedia && window.matchMedia("(pointer: coarse)").matches);
        } catch {
            return false;
        }
    }

    // ── Touch gesture recognizer (phase 7; docs/touch-interaction.md) ─────────────────────────
    // The decision table below is PURE: pointer kind + press origin + timing in, one action out. The
    // gesture SESSIONS come from the shared core helpers (pointer-drag.js for the single-pointer
    // press/tap/long-press, pointer-gesture.js for the two-finger pinch) — this file never implements
    // a drag or a pinch of its own. Keeping the recognizer pure is what makes the touch rules
    // node-testable (`npm run test:spreadsheet`) with no DOM at all.

    /** The touch long-press that opens the cell context menu (same 500 ms as the wireframe canvas). */
    const touchLongPressMs = 500;
    /** Two taps inside this window (and inside the slop) are a double tap → start editing. */
    const touchDoubleTapMs = 320;
    /** A finger that travelled further than this is a pan/scroll, not a tap. */
    const touchTapSlopPx = 24;
    /** Movement that turns a press on a touch handle into a drag (the cell surface never drags). */
    const touchDragThresholdPx = 8;
    /** Pinch zoom lives on the component's own zoom grid (0.5–2.0 in 10 % steps). */
    const pinchZoomMin = 0.5;
    const pinchZoomMax = 2.0;
    const pinchZoomStep = 0.1;
    /** WCAG 2.5.7 / --tm-touch-target: the finger floor for a canvas hit area. */
    const coarseTouchHitPx = 44;

    function positiveOr(value, fallback) {
        const number = Number(value);
        return Number.isFinite(number) && number > 0 ? number : fallback;
    }

    /**
     * The only pointer-kind switch the canvas branches on. A pen and a mouse keep the pre-phase-7
     * click/drag path; only `touch` takes the touch gesture layer.
     */
    function resolvePointerKind(pointerType) {
        const kind = String(pointerType ?? "mouse").trim().toLowerCase();
        if (kind === "touch") return "touch";
        if (kind === "pen") return "pen";
        return "mouse";
    }

    /**
     * Tap recognition (state → action). A tap selects the cell, a second tap inside the double-tap
     * window starts editing, and a finger that travelled is a pan (`action: 'none'`).
     * @returns {{ action: 'select-cell'|'start-edit'|'native-mouse'|'none', tapCount: number,
     *             next: { time: number, x: number, y: number }|null }}
     */
    function resolveTouchTap(input) {
        const opts = input || {};
        const kind = resolvePointerKind(opts.pointerType);
        if (kind !== "touch") return { action: "native-mouse", tapCount: 1, next: null };

        const slop = positiveOr(opts.slopPx, touchTapSlopPx);
        const doubleTapMs = positiveOr(opts.doubleTapMs, touchDoubleTapMs);
        const now = Number(opts.now) || 0;
        const x = Number(opts.x) || 0;
        const y = Number(opts.y) || 0;
        if ((Number(opts.moveDistance) || 0) > slop) return { action: "none", tapCount: 0, next: null };

        const previous = opts.previous;
        if (previous
            && now - Number(previous.time) <= doubleTapMs
            && Math.hypot(x - Number(previous.x), y - Number(previous.y)) <= slop) {
            return { action: "start-edit", tapCount: 2, next: null };
        }

        return { action: "select-cell", tapCount: 1, next: { time: now, x, y } };
    }

    /** A date-like number format: a date cell must not open the decimal keypad. */
    function isDateLikeNumberFormat(format) {
        const text = String(format || "").toLowerCase();
        if (!text) return false;
        return /(yyyy|yy|dd|hh|ss)/.test(text) || /(^|[^a-z])[ydhms]([^a-z]|$)/.test(text);
    }

    /**
     * The virtual keyboard a cell editor asks for, derived from the cell's value and number format.
     * A numeric cell gets `inputmode="decimal"`; everything else (text, dates, formulas) gets `text`.
     */
    function resolveCellInputMode(cell) {
        if (!cell) return "text";
        const style = read(cell, "Style", null);
        const format = String(read(style, "NumberFormat", "") || "").trim().toLowerCase();
        if (format.includes("%")) return "decimal";
        if (isDateLikeNumberFormat(format)) return "text";
        const value = read(cell, "Value", "");
        if (typeof value === "number" && Number.isFinite(value)) return "decimal";
        if (typeof value === "string" && /^[-+]?\d+([.,]\d+)?\s*$/.test(value)) return "decimal";
        return "text";
    }

    /**
     * What a touch drag does, from the origin of the press. A drag from a selection handle extends
     * the selection, a drag from the fill handle fills (the pre-existing fill drag), and a drag
     * anywhere else is the browser's native pan — no JS gesture, no `preventDefault`.
     * @returns {{ action: 'extend-selection'|'fill-handle'|'canvas-owned'|'native-pan'|'native-mouse',
     *             kind: 'mouse'|'pen'|'touch', preventDefault: boolean }}
     */
    function resolveTouchDragAction(input) {
        const opts = input || {};
        const kind = resolvePointerKind(opts.pointerType);
        if (kind !== "touch") return { action: "native-mouse", kind, preventDefault: false };
        const origin = String(opts.origin ?? "");
        if (origin === "auto-fill") return { action: "fill-handle", kind, preventDefault: false };
        if (origin === "selection-handle") return { action: "extend-selection", kind, preventDefault: false };
        if (origin === "filter-button" || origin === "validation-drop"
            || origin === "resize" || origin === "overlay" || origin === "editor") {
            return { action: "canvas-owned", kind, preventDefault: false };
        }
        // A finger on a cell/header/empty area pans the grid: the browser scrolls, never the canvas.
        return { action: "native-pan", kind, preventDefault: false };
    }

    /** Long-press recognition: touch only, 500 ms by default. */
    function resolveLongPressAction(input) {
        const opts = input || {};
        const longPressMs = positiveOr(opts.longPressMs, touchLongPressMs);
        const kind = resolvePointerKind(opts.pointerType);
        const durationMs = Number(opts.durationMs) || 0;
        return {
            action: kind === "touch" && durationMs >= longPressMs ? "open-context-menu" : "none",
            longPressMs
        };
    }

    /**
     * Pinch → one clamped zoom value on the component's 10 % grid. The gesture reports a cumulative
     * scale from its start; the recognizer turns it into the absolute zoom factor .NET owns, and
     * `changed` is false for a tremor that never left the current step (no .NET call per frame).
     */
    function resolvePinchZoom(input) {
        const opts = input || {};
        const min = Number.isFinite(opts.minZoom) ? Number(opts.minZoom) : pinchZoomMin;
        const max = Number.isFinite(opts.maxZoom) ? Number(opts.maxZoom) : pinchZoomMax;
        const step = positiveOr(opts.step, pinchZoomStep);
        const start = positiveOr(opts.startZoom, 1);
        const scale = positiveOr(opts.scale, 1);
        const target = Math.min(max, Math.max(min, start * scale));
        const steps = (target - min) / step;
        const index = target >= start
            ? Math.floor(steps + 1e-9)
            : Math.ceil(steps - 1e-9);
        const zoom = Math.round((min + index * step) * 100) / 100;
        const previous = Math.round(start * 100) / 100;
        return {
            zoom,
            direction: zoom > previous ? 1 : zoom < previous ? -1 : 0,
            changed: Math.abs(zoom - previous) > 0.001,
            clamped: Math.abs(target - start * scale) > 0.001
        };
    }

    /**
     * The fill handle's HIT box (what a finger has to reach), from the density handle scale and the
     * pointer kind. The compact fine-pointer box stays the historic 10 px — the mouse path is
     * unchanged — while Comfortable grows it with the painted handle and a coarse pointer gets the
     * finger floor.
     */
    function resolveAutoFillHitSize(input) {
        const opts = input || {};
        const handleScale = positiveOr(opts.handleScale, 1);
        const hit = Math.max(10, Math.round(10 * handleScale));
        return opts.coarse === true ? Math.max(hit, 24) : hit;
    }

    /**
     * The DOM touch-handle rects for a selection rectangle (grid/scrolport coordinates): the four
     * edge drops that extend the selection plus the fill handle at the bottom-right corner. They are
     * real DOM boxes because `touch-action: none` can only be set on an element — a painted handle
     * cannot stop the browser from panning the grid under the finger.
     */
    function resolveTouchHandleRects(input) {
        const opts = input || {};
        const width = Number(opts.width) || 0;
        const height = Number(opts.height) || 0;
        if (!(width > 0) || !(height > 0)) {
            return { start: null, fill: null, end: null };
        }

        const x = Number(opts.x) || 0;
        const y = Number(opts.y) || 0;
        const coarse = opts.coarse === true;
        // Two ROUND corner drops (drag-only, the selection extender) plus the fill handle. Full edge
        // strips were rejected: on a phone (30px rows) a strip along the selection's edge swallowed the
        // taps and long-presses meant for the selected row/column itself.
        const size = Math.max(20, Math.round(Number(opts.hitSize) || (coarse ? 28 : 20)));
        const half = size / 2;
        // The bottom-right drop sits just outside the fill handle so the two never share a centre: the
        // handle keeps Excel's corner (fill), the drop is the extend gesture next to it.
        const offset = Math.round(size * 0.6);
        return {
            start: { x: x - half, y: y - half, width: size, height: size },
            fill: { x: x + width - half, y: y + height - half, width: size, height: size },
            end: { x: x + width + offset - half, y: y + height + offset - half, width: size, height: size }
        };
    }

    /**
     * Clips a handle box to the grid BODY (never over the frozen gutters). A box that would have to be
     * SHIFTED onto the selection to fit is hidden instead: the drops must not cover the selected cells,
     * and a sliver below `minSize` is not a finger target anyway.
     */
    function clipTouchHandleRect(input) {
        const opts = input || {};
        const rect = opts.rect;
        const bounds = opts.bounds;
        const minSize = Math.max(0, Number(opts.minSize) || 16);
        if (!rect || !bounds) return null;
        const left = Math.max(rect.x, bounds.x);
        const top = Math.max(rect.y, bounds.y);
        const right = Math.min(rect.x + rect.width, bounds.x + bounds.width);
        const bottom = Math.min(rect.y + rect.height, bounds.y + bounds.height);
        if (right - left < minSize || bottom - top < minSize) return null;
        return { x: left, y: top, width: right - left, height: bottom - top };
    }

    /**
     * The header filter button's hit rect. A fine pointer keeps the painted glyph rect exactly (no
     * mouse regression); a coarse pointer grows the hit box to the touch floor, filling the header
     * gutter and never leaving its own column.
     */
    function resolveFilterButtonHitRect(input) {
        const opts = input || {};
        const rect = opts.rect || {};
        const x = Number(rect.x) || 0;
        const y = Number(rect.y) || 0;
        const w = Math.max(0, Number(rect.w ?? rect.width) || 0);
        const h = Math.max(0, Number(rect.h ?? rect.height) || 0);
        if (opts.coarse !== true) return { x, y, width: w, height: h };

        const columnWidth = Math.max(w, Number(opts.columnWidth) || w);
        const headerHeight = Math.max(h, Number(opts.headerHeight) || h);
        const width = Math.min(columnWidth, Math.max(coarseTouchHitPx, w));
        const height = Math.min(headerHeight, Math.max(coarseTouchHitPx, h));
        const columnLeft = x + w - columnWidth;
        const left = Math.max(columnLeft, Math.min(Math.round(x + w / 2 - width / 2), x + w - width));
        const top = Math.max(0, Math.min(Math.round(y + h / 2 - height / 2), headerHeight - height));
        return { x: left, y: top, width, height };
    }

    /**
     * SP1: resolves the requested grid density to the render scales the canvas paints with.
     * Auto means "comfortable on a coarse pointer (touch), compact elsewhere".
     */
    function resolveDensity(density, coarsePointer) {
        const requested = String(density ?? "").trim().toLowerCase();
        const coarse = coarsePointer == null ? isCoarsePointer() : !!coarsePointer;
        const comfortable = requested === "comfortable" || (requested !== "compact" && coarse);
        return {
            density: comfortable ? "comfortable" : "compact",
            comfortable,
            rowScale: comfortable ? comfortableRowScale : 1,
            fontScale: comfortable ? comfortableFontScale : 1,
            handleScale: comfortable ? comfortableHandleScale : 1,
            gutterScale: comfortable ? comfortableGutterScale : 1
        };
    }

    /**
     * The density scales for a frame. The COMPONENT resolved them (`SpreadsheetDensityScale`) and ships
     * them in the frame, so the canvas paints with exactly the scale the row geometry used; the JS rule
     * is only the fallback for a frame without them (or a standalone canvas host).
     */
    function resolveDensityFromModel(model) {
        const requested = String(read(model, "Density", "auto"));
        const coarse = !!read(model, "PointerCoarse", false);
        const resolved = resolveDensity(requested, coarse);
        const scale = (value, fallback) => {
            const number = Number(value);
            return Number.isFinite(number) && number > 0 ? number : fallback;
        };
        return {
            density: resolved.density,
            comfortable: resolved.comfortable,
            rowScale: scale(read(model, "DensityRowScale", 0), resolved.rowScale),
            fontScale: scale(read(model, "DensityFontScale", 0), resolved.fontScale),
            handleScale: scale(read(model, "DensityHandleScale", 0), resolved.handleScale),
            gutterScale: scale(read(model, "DensityGutterScale", 0), resolved.gutterScale)
        };
    }

    /**
     * The header-label font at a density scale. The token is a CSS font SHORTHAND
     * (`500 11px Inter, sans-serif`), so the size token inside it is scaled in place and the token
     * itself is never rewritten (dimension + unit + family stay exactly what the host set).
     */
    function resolveHeaderFont(font, fontScale) {
        const value = String(font || "");
        const scale = Number(fontScale);
        if (!value.trim() || !Number.isFinite(scale) || scale <= 0 || scale === 1) return value;
        const size = /(^|\s)(\d*\.?\d+)(px|pt)(?=\s|\/|$)/.exec(value);
        if (!size) return value;
        const scaled = Math.round(Number(size[2]) * scale * 100) / 100;
        return `${value.slice(0, size.index + size[1].length)}${scaled}${size[3]}${value.slice(size.index + size[0].length)}`;
    }

    /**
     * The grid-body point of a client press: the header gutters and the logical scroll offset are the
     * ONLY inputs, so a scaled gutter (Comfortable 56/28) shifts the hit path exactly the way it shifts
     * the painted cells. `toContentPoint` feeds it the frame's own RowHeaderWidth/ColumnHeaderHeight.
     */
    function resolveContentPoint(input) {
        const opts = input || {};
        const number = (value) => (Number.isFinite(Number(value)) ? Number(value) : 0);
        return {
            x: number(opts.clientX) - number(opts.rectLeft) + number(opts.scrollLeft) - number(opts.rowHeaderWidth),
            y: number(opts.clientY) - number(opts.rectTop) + number(opts.scrollTop) - number(opts.columnHeaderHeight)
        };
    }

    function getDensityScales(root) {
        const s = getState(root);
        const model = s?.model;
        const key = [
            read(model, "Density", "auto"),
            read(model, "PointerCoarse", false) ? 1 : 0,
            read(model, "DensityRowScale", 0),
            read(model, "DensityFontScale", 0),
            read(model, "DensityHandleScale", 0),
            read(model, "DensityGutterScale", 0)
        ].join("|");
        if (s?.densityScales?.key === key) return s.densityScales;
        const scales = { key, ...resolveDensityFromModel(model) };
        if (s) s.densityScales = scales;
        return scales;
    }

    /**
     * The active row/column header ranges of a selection. Pure: the canvas passes the local selection
     * snapshot (the model's copy lags a drag), the node lane passes a plain object.
     */
    function resolveHeaderHighlight(selection) {
        if (!selection) {
            return {
                hasSelection: false,
                activeRow: -1,
                activeCol: -1,
                activeRows: { start: -1, end: -1 },
                activeColumns: { start: -1, end: -1 }
            };
        }

        const number = (value, fallback) => (Number.isFinite(Number(value)) ? Number(value) : fallback);
        const activeRow = number(selection.row, -1);
        const activeCol = number(selection.col, -1);
        const startRow = number(selection.startRow, activeRow);
        const startCol = number(selection.startCol, activeCol);
        const endRow = number(selection.endRow, activeRow);
        const endCol = number(selection.endCol, activeCol);

        return {
            hasSelection: true,
            activeRow,
            activeCol,
            activeRows: { start: Math.min(startRow, endRow), end: Math.max(startRow, endRow) },
            activeColumns: { start: Math.min(startCol, endCol), end: Math.max(startCol, endCol) }
        };
    }

    function isHighlightedIndex(range, index) {
        return !!range && index >= range.start && index <= range.end;
    }

    function createDebugMetrics() {
        return {
            redrawCount: 0,
            selectionRedrawCount: 0,
            bitmapShiftCount: 0,
            bitmapShiftFallbackCount: 0,
            dragAutoscrollFrames: 0,
            resizePointerMoveCount: 0,
            resizePreviewFrameCount: 0,
            resizePaintFrameCount: 0,
            resizeDotNetCallbackCount: 0,
            resizeBlazorFrameCount: 0,
            dotNetCallbackCount: 0,
            hotPathDotNetCallbackCount: 0,
            dotNetCallbacksByMethod: {},
            hotPathDotNetCallbacksByMethod: {},
            lastDotNetCallbackMethod: "",
            blazorFrameCount: 0,
            hotPathBlazorFrameCount: 0,
            lastBlazorFrameAgeMs: 0,
            localRevision: 0,
            blazorRevision: 0,
            serverRevision: 0,
            cellStoreSize: 0,
            cellStoreRevision: 0,
            cellStoreLastFrameCellCount: 0,
            cellStoreFormulaRefCount: 0,
            cellStoreStyledOrNonEmptyCount: 0,
            cellStoreMergedCount: 0,
            cellStoreLookupCount: 0,
            cellStoreHitCount: 0,
            cellStoreMissCount: 0,
            cellStoreFrameScanCount: 0,
            cellStoreVisibleCellCount: 0,
            cellStoreSetCellCount: 0,
            cellStoreClearCellCount: 0,
            viewportCallbackCount: 0,
            selectionCallbackCount: 0,
            keyCommandCallbackCount: 0,
            paintRequestCount: 0,
            paintFrameCount: 0,
            selectionPaintFrameCount: 0,
            contentPaintFrameCount: 0,
            mergedPaintRequestCount: 0,
            discardedIntermediatePaintCount: 0,
            maxMergedPaintRequestsPerFrame: 0,
            syncRequestCount: 0,
            nativeScrollEventCount: 0,
            scrollToCount: 0,
            keyboardScrollToCount: 0,
            programmaticEnsureScrollToCount: 0,
            logicalScrollLeft: 0,
            logicalScrollTop: 0,
            nativeScrollLeft: 0,
            nativeScrollTop: 0,
            logicalKeyboardScrollCount: 0,
            logicalNativeScrollCount: 0,
            logicalProgrammaticScrollCount: 0,
            logicalWheelScrollCount: 0,
            logicalPointerScrollCount: 0,
            wheelEventCount: 0,
            wheelPreventedCount: 0,
            logicalNativeSyncCount: 0,
            ignoredOwnNativeScrollEventCount: 0,
            ownNativeScrollEventCount: 0,
            userNativeScrollEventCount: 0,
            wheelNativeScrollEventCount: 0,
            scrollbarNativeScrollEventCount: 0,
            staleNativeScrollEventCount: 0,
            keyboardInteractions: 0,
            keyboardRepeatAccelerationEnabled: keyboardRepeatAccelerationEnabledDefault,
            keyboardRepeatSequenceCount: 0,
            keyboardRepeatResetCount: 0,
            keyboardRepeatEventCount: 0,
            keyboardRepeatAcceleratedEventCount: 0,
            keyboardRepeatLastKey: "",
            keyboardRepeatLastStep: 1,
            keyboardRepeatMaxStep: 1,
            scrollInteractions: 0,
            pointerInteractions: 0,
            staleFramesIgnored: 0,
            slowFramesOver16: 0,
            slowFramesOver33: 0,
            lastRedrawSource: "",
            lastRedrawKind: "",
            lastDrawMs: 0,
            lastSelectionDrawMs: 0,
            lastBitmapShiftDx: 0,
            lastBitmapShiftDy: 0,
            lastBitmapShiftReason: "",
            lastTextDrawMs: 0,
            lastDrawCellsMs: 0,
            lastDrawCellContentMs: 0,
            lastVisibleCellCount: 0,
            lastTextCount: 0,
            lastContextSaveClipRestoreCount: 0,
            lastContextSaveMs: 0,
            lastContextClipMs: 0,
            lastContextRestoreMs: 0,
            ensureCellVisibleCount: 0,
            ensureCellVisibleTotalMs: 0,
            drawCellsTotalMs: 0,
            drawCellContentTotalMs: 0,
            contextSaveClipRestoreTotalCount: 0,
            contextSaveTotalMs: 0,
            contextClipTotalMs: 0,
            contextRestoreTotalMs: 0,
            fastCellPathCount: 0,
            slowCellPathCount: 0,
            skippedEmptyCellContentCount: 0,
            unclippedTextCount: 0,
            clippedTextCount: 0,
            contextStateSetCount: 0,
            contextStateSkipCount: 0,
            cellSnapshotHitCount: 0,
            cellSnapshotMissCount: 0,
            cellSnapshotStoreCount: 0,
            cellSnapshotInvalidationCount: 0,
            cellSnapshotCacheSize: 0,
            selectionDirtyRectCount: 0,
            contentDirtyRectCount: 0,
            selectionDirtyRectArea: 0,
            contentDirtyRectArea: 0,
            contentLayerPaintCount: 0,
            headerLayerPaintCount: 0,
            selectionLayerPaintCount: 0,
            editorLayerUpdateCount: 0,
            lastContentLayerMs: 0,
            lastHeaderLayerMs: 0,
            lastSelectionLayerMs: 0,
            lastEditorLayerMs: 0,
            contentFramesOver16: 0,
            contentFramesOver33: 0,
            selectionFramesOver16: 0,
            selectionFramesOver33: 0,
            commandLogQueuedCount: 0,
            commandLogBatchCallbackCount: 0,
            commandLogBatchItemCount: 0,
            commandLogAckRevision: 0,
            commandLogRetryCount: 0,
            commandLogObsoleteDropCount: 0,
            cellChangedCommandCount: 0,
            rangeChangedCommandCount: 0,
            selectionSettledCommandCount: 0,
            viewportSettledCommandCount: 0,
            formulaCommittedCommandCount: 0,
            columnResizeCommandCount: 0,
            rowResizeCommandCount: 0,
            editorOpenCount: 0,
            editorCancelCount: 0,
            editorLocalCommitCount: 0,
            cellEditCommitQueuedCount: 0,
            cellEditCommitBatchCallbackCount: 0,
            cellEditCommitBatchItemCount: 0,
            formulaEditorActivationCount: 0,
            formulaEditorReferenceParseCount: 0,
            formulaEditorReferenceCount: 0,
            formulaEditorCellClickInsertCount: 0,
            formulaEditorRangeDragCount: 0,
            formulaEditorHighlightCount: 0,
            formulaEditorCaretMoveCount: 0,
            formulaEditorTokenReplaceCount: 0,
            formulaEditorIgnoredSelfClickCount: 0,
            formulaEditorArrowCaretCount: 0,
            editorRemoveAttemptCount: 0,
            editorRemoveCompleteCount: 0,
            editorLastDomCountAfterRemove: 0,
            textMeasureCacheSize: 0,
            fontStringCacheSize: 0,
            paintStyleCacheSize: 0,
            displayValueCacheSize: 0,
            visibleRowCount: 0,
            visibleColumnCount: 0,
            visibleLayoutCacheHits: 0,
            visibleLayoutCacheMisses: 0,
            visibleLayoutInvalidationCount: 0,
            visibleLayoutResizeInvalidationCount: 0,
            visibleLayoutFreezeInvalidationCount: 0,
            visibleLayoutJsComputeCount: 0,
            visibleLayoutBinarySearchCount: 0,
            layoutRowSizeCacheSize: 0,
            layoutColumnSizeCacheSize: 0,
            layoutRevision: 0,
            layoutOverscanRows: defaultLayoutOverscanRows,
            layoutOverscanColumns: defaultLayoutOverscanColumns
        };
    }

    function toContentPoint(root, ev) {
        const s = getState(root);
        const model = s?.model || {};
        const rect = root.getBoundingClientRect();
        // The gutters come from the FRAME (the component scaled them per density), never from a
        // constant: a 56/28 Comfortable gutter shifts the hit path exactly as it shifts the paint.
        return resolveContentPoint({
            clientX: ev.clientX,
            clientY: ev.clientY,
            rectLeft: rect.left,
            rectTop: rect.top,
            scrollLeft: getLogicalScrollLeft(root),
            scrollTop: getLogicalScrollTop(root),
            rowHeaderWidth: read(model, "RowHeaderWidth", 40),
            columnHeaderHeight: read(model, "ColumnHeaderHeight", 20)
        });
    }

    function screenX(model, col) {
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const scrollLeft = read(model, "ScrollLeft", 0);
        return rowHeaderWidth + read(col, "Left", 0) - (read(col, "Frozen", false) ? 0 : scrollLeft);
    }

    function screenY(model, row) {
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const scrollTop = read(model, "ScrollTop", 0);
        return columnHeaderHeight + read(row, "Top", 0) - (read(row, "Frozen", false) ? 0 : scrollTop);
    }

    function hitResize(root, ev) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return null;

        const rect = root.getBoundingClientRect();
        const x = ev.clientX - rect.left;
        const y = ev.clientY - rect.top;
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const layout = getVisibleLayout(root, model, root.clientWidth || read(model, "ViewportWidth", 1), root.clientHeight || read(model, "ViewportHeight", 1));

        if (y <= columnHeaderHeight && x >= rowHeaderWidth) {
            for (const col of layout.columns) {
                const right = read(col, "x", screenX(model, col)) + read(col, "Width", 0);
                if (Math.abs(x - right) <= 4) {
                    return { kind: "column", index: read(col, "Index", 0), start: ev.clientX, size: read(col, "Width", 64) };
                }
            }
        }

        if (x <= rowHeaderWidth && y >= columnHeaderHeight) {
            for (const row of layout.rows) {
                const bottom = read(row, "y", screenY(model, row)) + read(row, "Height", 0);
                if (Math.abs(y - bottom) <= 4) {
                    return { kind: "row", index: read(row, "Index", 0), start: ev.clientY, size: read(row, "Height", 20) };
                }
            }
        }

        return null;
    }

    function getResizePreviewState(root) {
        return getState(root)?.resize || null;
    }

    function beginResizeSession(root, hit, ev) {
        const s = getState(root);
        if (!s || !hit) return;
        const client = hit.kind === "column" ? ev.clientX : ev.clientY;
        s.resize = {
            kind: hit.kind,
            index: hit.index,
            start: hit.start,
            size: hit.size,
            currentSize: hit.size,
            pointerId: ev.pointerId,
            previewClient: client,
            previewFrame: 0
        };
        markLocalInteraction(root, "resize");
        requestCanvasRedraw(root, "resize-preview-start", "selection");
    }

    function scheduleResizePreview(root) {
        const s = getState(root);
        const resize = s?.resize;
        if (!s || !resize || resize.previewFrame) return;
        resize.previewFrame = requestAnimationFrame(() => {
            const current = getResizePreviewState(root);
            if (!current) return;
            current.previewFrame = 0;
            if (s.metrics) {
                s.metrics.resizePreviewFrameCount += 1;
                s.metrics.resizePaintFrameCount += 1;
            }
            requestCanvasRedraw(root, "resize-preview", "selection");
        });
    }

    function updateResizeSession(root, ev) {
        const s = getState(root);
        const resize = s?.resize;
        if (!s || !resize) return false;
        const client = resize.kind === "column" ? ev.clientX : ev.clientY;
        const delta = client - resize.start;
        const next = Math.max(resize.kind === "column" ? 16 : 8, resize.size + delta);
        resize.previewClient = client;
        if (Math.abs((resize.currentSize || 0) - next) <= 0.25) return false;
        resize.currentSize = next;
        if (s.metrics) s.metrics.resizePointerMoveCount += 1;
        scheduleResizePreview(root);
        return true;
    }

    function clearResizePreview(root) {
        const s = getState(root);
        const resize = s?.resize;
        if (!s || !resize) return;
        if (resize.previewFrame) {
            cancelAnimationFrame(resize.previewFrame);
            resize.previewFrame = 0;
        }
        s.resize = null;
        requestCanvasRedraw(root, "resize-preview-clear", "selection");
    }

    function hitCell(root, point) {
        const s = getState(root);
        const model = s?.model;
        if (!model || point.x < 0 || point.y < 0) return null;

        const layout = getLayoutState(root);
        const rowOffsets = layout ? getRowOffsets(layout) : null;
        const colOffsets = layout ? getColumnOffsets(layout) : null;
        const rowIndex = rowOffsets ? binarySearchOffset(rowOffsets, point.y) : -1;
        const colIndex = colOffsets ? binarySearchOffset(colOffsets, point.x) : -1;
        if (s.metrics && rowOffsets && colOffsets) s.metrics.visibleLayoutBinarySearchCount += 2;
        if (rowIndex >= 0 && colIndex >= 0) {
            return {
                row: rowIndex,
                col: colIndex,
                cell: findCell(model, rowIndex, colIndex)
            };
        }

        const row = findFrameAt(read(model, "Rows", []), point.y, "Top", "Height");
        const col = findFrameAt(read(model, "Columns", []), point.x, "Left", "Width");
        if (!row || !col) return null;

        const fallbackRowIndex = read(row, "Index", -1);
        const fallbackColIndex = read(col, "Index", -1);
        if (fallbackRowIndex < 0 || fallbackColIndex < 0) return null;

        return {
            row: fallbackRowIndex,
            col: fallbackColIndex,
            cell: findCell(model, fallbackRowIndex, fallbackColIndex)
        };
    }

    function hitAutoFillHandle(root, ev) {
        const snapshot = getSelectionSnapshot(root);
        const bounds = selectionBounds(snapshot);
        const rect = getCellScreenRect(root, bounds.endRow, bounds.endCol);
        if (!rect) return false;
        const rootRect = root.getBoundingClientRect();
        // SP1 + phase 7: the HIT box grows with the density and reaches the finger floor on a coarse
        // pointer (the painted handle alone was still a 10px target on a tablet). A compact fine
        // pointer keeps exactly the pre-phase-7 10px box, so the mouse path is unchanged.
        const handleSize = resolveAutoFillHitSize({
            handleScale: getDensityScales(root).handleScale,
            coarse: read(getState(root)?.model, "PointerCoarse", false) === true
        });
        const left = rootRect.left + rect.x + rect.width - handleSize;
        const top = rootRect.top + rect.y + rect.height - handleSize;
        return ev.clientX >= left
            && ev.clientX <= left + handleSize + 2
            && ev.clientY >= top
            && ev.clientY <= top + handleSize + 2;
    }

    function sameCell(a, b) {
        if (!a && !b) return true;
        if (!a || !b) return false;
        return a.row === b.row && a.col === b.col;
    }

    function parseCellRef(cellRef) {
        const match = /^([A-Z]+)(\d+)$/i.exec(String(cellRef || "A1"));
        if (!match) return { row: 0, col: 0 };

        let col = 0;
        for (const ch of match[1].toUpperCase()) {
            col = col * 26 + ch.charCodeAt(0) - 64;
        }

        return { row: Math.max(0, Number(match[2]) - 1), col: Math.max(0, col - 1) };
    }

    function normalizeFormulaCellRef(value) {
        return String(value || "").replace(/\$/g, "").toUpperCase();
    }

    function parseFormulaReferenceToken(token) {
        const raw = normalizeFormulaCellRef(token);
        const parts = raw.split(":");
        if (parts.length < 1 || parts.length > 2) return null;
        const first = /^([A-Z]{1,3})(\d{1,7})$/.exec(parts[0]);
        const second = parts.length === 2 ? /^([A-Z]{1,3})(\d{1,7})$/.exec(parts[1]) : first;
        if (!first || !second) return null;
        const start = parseCellRef(parts[0]);
        const end = parseCellRef(parts[1] || parts[0]);
        return {
            raw,
            startRow: Math.min(start.row, end.row),
            startCol: Math.min(start.col, end.col),
            endRow: Math.max(start.row, end.row),
            endCol: Math.max(start.col, end.col)
        };
    }

    function isFormulaIdentifierBoundary(ch) {
        return !ch || !/[A-Za-z0-9_$]/.test(ch);
    }

    function readFormulaCellReference(text, start) {
        let index = start;
        if (text[index] === "$") index += 1;

        const lettersStart = index;
        while (index < text.length && /[A-Za-z]/.test(text[index]) && index - lettersStart < 3) {
            index += 1;
        }

        const lettersLength = index - lettersStart;
        if (lettersLength < 1 || lettersLength > 3) return null;

        if (text[index] === "$") index += 1;

        const digitsStart = index;
        while (index < text.length && /\d/.test(text[index]) && index - digitsStart < 7) {
            index += 1;
        }

        const digitsLength = index - digitsStart;
        if (digitsLength < 1 || digitsLength > 7) return null;

        return {
            text: text.slice(start, index),
            start,
            end: index
        };
    }

    function readFormulaReferenceLexeme(text, start) {
        const first = readFormulaCellReference(text, start);
        if (!first) return null;

        let end = first.end;
        let tokenText = first.text;
        let type = "reference";
        if (text[end] === ":") {
            const second = readFormulaCellReference(text, end + 1);
            if (second) {
                end = second.end;
                tokenText = text.slice(start, end);
                type = "range";
            }
        }

        const before = text[start - 1] || "";
        const after = text[end] || "";
        if (!isFormulaIdentifierBoundary(before) || !isFormulaIdentifierBoundary(after)) {
            return null;
        }

        return { type, text: tokenText, start, end };
    }

    function tokenizeFormula(text) {
        const value = String(text || "");
        const tokens = [];
        let index = 0;

        while (index < value.length) {
            const ch = value[index];

            if (/\s/.test(ch)) {
                const start = index;
                index += 1;
                while (index < value.length && /\s/.test(value[index])) index += 1;
                tokens.push({ type: "whitespace", text: value.slice(start, index), start, end: index });
                continue;
            }

            if (ch === "\"") {
                const start = index;
                index += 1;
                while (index < value.length) {
                    if (value[index] === "\"") {
                        if (value[index + 1] === "\"") {
                            index += 2;
                            continue;
                        }
                        index += 1;
                        break;
                    }
                    index += 1;
                }
                tokens.push({ type: "string", text: value.slice(start, index), start, end: index });
                continue;
            }

            if (ch === "," || ch === ";") {
                tokens.push({ type: "separator", text: ch, start: index, end: index + 1 });
                index += 1;
                continue;
            }

            if (ch === "(" || ch === ")") {
                tokens.push({ type: "paren", text: ch, start: index, end: index + 1 });
                index += 1;
                continue;
            }

            if ((ch === "<" || ch === ">") && value[index + 1] === "=") {
                tokens.push({ type: "operator", text: value.slice(index, index + 2), start: index, end: index + 2 });
                index += 2;
                continue;
            }

            if (ch === "<" && value[index + 1] === ">") {
                tokens.push({ type: "operator", text: "<>", start: index, end: index + 2 });
                index += 2;
                continue;
            }

            if (/[=+\-*/^&<>:%]/.test(ch)) {
                tokens.push({ type: "operator", text: ch, start: index, end: index + 1 });
                index += 1;
                continue;
            }

            if (/\d/.test(ch) || (ch === "." && /\d/.test(value[index + 1] || ""))) {
                const start = index;
                index += 1;
                while (index < value.length && /[\d.]/.test(value[index])) index += 1;
                tokens.push({ type: "number", text: value.slice(start, index), start, end: index });
                continue;
            }

            const referenceToken = (ch === "$" || /[A-Za-z]/.test(ch))
                ? readFormulaReferenceLexeme(value, index)
                : null;
            if (referenceToken) {
                tokens.push(referenceToken);
                index = referenceToken.end;
                continue;
            }

            if (/[A-Za-z_]/.test(ch)) {
                const start = index;
                index += 1;
                while (index < value.length && /[A-Za-z0-9_.]/.test(value[index])) index += 1;
                const end = index;
                let probe = index;
                while (probe < value.length && /\s/.test(value[probe])) probe += 1;
                tokens.push({
                    type: probe < value.length && value[probe] === "(" ? "function" : "identifier",
                    text: value.slice(start, end),
                    start,
                    end
                });
                continue;
            }

            tokens.push({ type: "unknown", text: ch, start: index, end: index + 1 });
            index += 1;
        }

        return tokens;
    }

    function parseFormulaReferences(text) {
        const refs = [];
        for (const token of tokenizeFormula(text)) {
            if (token.type !== "reference" && token.type !== "range") continue;
            const parsed = parseFormulaReferenceToken(token.text);
            if (!parsed) continue;
            refs.push({
                ...parsed,
                text: token.text,
                start: token.start,
                end: token.end,
                colorIndex: refs.length % 6
            });
        }

        return refs;
    }

    function cycleSingleAbsoluteReference(cellRef) {
        const match = /^(\$?)([A-Za-z]{1,3})(\$?)(\d{1,7})$/i.exec(String(cellRef || ""));
        if (!match) return String(cellRef || "");

        const colAbs = match[1] === "$";
        const col = match[2].toUpperCase();
        const rowAbs = match[3] === "$";
        const row = match[4];

        let nextColAbs;
        let nextRowAbs;
        if (!colAbs && !rowAbs) {
            nextColAbs = true;
            nextRowAbs = true;
        } else if (colAbs && rowAbs) {
            nextColAbs = false;
            nextRowAbs = true;
        } else if (!colAbs && rowAbs) {
            nextColAbs = true;
            nextRowAbs = false;
        } else {
            nextColAbs = false;
            nextRowAbs = false;
        }

        return `${nextColAbs ? "$" : ""}${col}${nextRowAbs ? "$" : ""}${row}`;
    }

    function cycleAbsoluteReferenceToken(token) {
        const value = String(token || "");
        const parts = value.split(":");
        if (parts.length === 2) {
            return `${cycleSingleAbsoluteReference(parts[0])}:${cycleSingleAbsoluteReference(parts[1])}`;
        }

        return cycleSingleAbsoluteReference(value);
    }

    function getFormulaTokenAtCaret(refs, caret) {
        const position = Math.max(0, Number(caret) || 0);
        let previous = null;
        for (const ref of refs || []) {
            if (position >= ref.start && position <= ref.end) return ref;
            if (ref.end <= position) previous = ref;
        }

        return previous && previous.end === position ? previous : null;
    }

    function clampFormulaSelectionPosition(value, text) {
        const length = String(text || "").length;
        const numeric = Number(value);
        if (!Number.isFinite(numeric)) return length;
        return Math.max(0, Math.min(length, Math.floor(numeric)));
    }

    function getFormulaTokenSelection(refs, selectionStart, selectionEnd, text) {
        const normalizedText = String(text || "");
        const start = clampFormulaSelectionPosition(selectionStart, normalizedText);
        const end = clampFormulaSelectionPosition(selectionEnd, normalizedText);
        const caretToken = getFormulaTokenAtCaret(refs, start);
        let selectionToken = null;

        if (end > start) {
            selectionToken = (refs || []).find(ref => start >= ref.start && start <= ref.end && end >= ref.start && end <= ref.end)
                || (refs || []).find(ref => start < ref.end && end > ref.start)
                || (refs || []).find(ref => start >= ref.start && start <= ref.end)
                || null;
        }

        const activeToken = selectionToken || caretToken;
        const activeTokenIndex = activeToken ? (refs || []).findIndex(ref => ref.start === activeToken.start && ref.end === activeToken.end) : -1;
        return {
            selectionStart: start,
            selectionEnd: end,
            caretToken,
            selectionToken,
            activeToken,
            activeTokenIndex
        };
    }

    function findFrameAt(frames, offset, startName, sizeName) {
        for (const frame of frames) {
            const start = read(frame, startName, 0);
            const size = read(frame, sizeName, 0);
            if (size > 0 && offset >= start && offset < start + size) {
                return frame;
            }
        }

        return null;
    }

    function getFrameByIndex(frames, index) {
        for (const frame of frames) {
            if (read(frame, "Index", -1) === index) return frame;
        }

        return null;
    }

    function findIndexAtContentOffset(frames, offset, startName, sizeName, indexName, minIndex, maxIndex) {
        const frame = findFrameAt(frames, offset, startName, sizeName);
        if (frame) return Math.max(minIndex, Math.min(maxIndex, read(frame, indexName, minIndex)));

        if (!frames || frames.length === 0) {
            return Math.max(minIndex, Math.min(maxIndex, Math.floor(offset)));
        }

        let first = null;
        let last = null;
        let totalSize = 0;
        let sizeCount = 0;
        for (const item of frames) {
            const size = read(item, sizeName, 0);
            if (size <= 0) continue;
            if (!first || read(item, startName, 0) < read(first, startName, 0)) first = item;
            if (!last || read(item, startName, 0) > read(last, startName, 0)) last = item;
            totalSize += size;
            sizeCount += 1;
        }

        if (!first || !last || sizeCount === 0) return minIndex;

        const averageSize = Math.max(1, totalSize / sizeCount);
        const firstStart = read(first, startName, 0);
        const firstIndex = read(first, indexName, minIndex);
        const lastStart = read(last, startName, 0);
        const lastSize = read(last, sizeName, averageSize);
        const lastIndex = read(last, indexName, minIndex);
        let index;
        if (offset < firstStart) {
            index = firstIndex - Math.ceil((firstStart - offset) / averageSize);
        } else {
            index = lastIndex + Math.floor(Math.max(0, offset - (lastStart + lastSize)) / averageSize) + 1;
        }

        return Math.max(minIndex, Math.min(maxIndex, index));
    }

    function findCell(model, row, col) {
        const root = modelRootMap.get(model);
        const store = getCellStore(root);
        const metrics = getState(root)?.metrics;
        if (metrics) metrics.cellStoreLookupCount += 1;
        if (store) {
            const cell = store.cells.get(cellStoreKey(row, col));
            if (cell) {
                if (metrics) metrics.cellStoreHitCount += 1;
                return cell;
            }
            if (metrics) metrics.cellStoreMissCount += 1;
        }

        if (metrics) metrics.cellStoreFrameScanCount += 1;
        for (const cell of read(model, "Cells", [])) {
            if (read(cell, "Row", -1) === row && read(cell, "Col", -1) === col) {
                return cell;
            }
        }

        return null;
    }

    function getDrawableCells(root, model, layout) {
        const store = getCellStore(root);
        const metrics = getState(root)?.metrics;
        if (!store) return read(model, "Cells", []);

        const visible = [];
        for (const rowFrame of layout?.rows || []) {
            const row = read(rowFrame, "Index", -1);
            for (const columnFrame of layout?.columns || []) {
                const col = read(columnFrame, "Index", -1);
                const key = cellStoreKey(row, col);
                if (metrics) metrics.cellStoreLookupCount += 1;
                const cell = store.cells.get(key);
                if (cell) {
                    if (metrics) metrics.cellStoreHitCount += 1;
                    const next = { ...cell };
                    // A collapsed (hidden) row/column frame is authoritative: force the cell to 0 so a
                    // stale cell-store entry left behind by a now-hidden row does not keep rendering at
                    // its old size. For visible frames keep the cell's own size (preserves merged cells).
                    const frameWidth = read(columnFrame, "Width", 0);
                    const frameHeight = read(rowFrame, "Height", 0);
                    const width = frameWidth === 0 ? 0 : read(cell, "Width", frameWidth);
                    const height = frameHeight === 0 ? 0 : read(cell, "Height", frameHeight);
                    write(next, "Row", row);
                    write(next, "Col", col);
                    write(next, "Left", read(columnFrame, "Left", 0));
                    write(next, "Top", read(rowFrame, "Top", 0));
                    write(next, "Width", width);
                    write(next, "Height", height);
                    visible.push(next);
                } else if (metrics) {
                    metrics.cellStoreMissCount += 1;
                }
            }
        }

        if (metrics) metrics.cellStoreVisibleCellCount = visible.length;
        return visible;
    }

    function getCellScreenRect(root, row, col) {
        const s = getState(root);
        const model = s?.model;
        if (!s || !model) return null;
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        const layout = getVisibleLayout(root, model, width, height);
        const rowFrame = getFrameByIndex(layout.rows, row);
        const colFrame = getFrameByIndex(layout.columns, col);
        if (!rowFrame || !colFrame) return null;
        return {
            x: read(colFrame, "x", 0),
            y: read(rowFrame, "y", 0),
            width: read(colFrame, "Width", 0),
            height: read(rowFrame, "Height", 0)
        };
    }

    function getSelectionScreenRect(root, snapshot) {
        if (!snapshot) return null;
        const startRow = Math.min(snapshot.startRow ?? snapshot.row, snapshot.endRow ?? snapshot.row);
        const endRow = Math.max(snapshot.startRow ?? snapshot.row, snapshot.endRow ?? snapshot.row);
        const startCol = Math.min(snapshot.startCol ?? snapshot.col, snapshot.endCol ?? snapshot.col);
        const endCol = Math.max(snapshot.startCol ?? snapshot.col, snapshot.endCol ?? snapshot.col);
        const topLeft = getCellScreenRect(root, startRow, startCol);
        const bottomRight = getCellScreenRect(root, endRow, endCol);
        const active = getCellScreenRect(root, snapshot.row, snapshot.col);
        const rects = [topLeft, bottomRight, active].filter(Boolean);
        if (rects.length === 0) return null;
        let left = Infinity;
        let top = Infinity;
        let right = -Infinity;
        let bottom = -Infinity;
        for (const rect of rects) {
            left = Math.min(left, rect.x);
            top = Math.min(top, rect.y);
            right = Math.max(right, rect.x + rect.width);
            bottom = Math.max(bottom, rect.y + rect.height);
        }
        return { x: left, y: top, width: right - left, height: bottom - top };
    }

    function addSelectionDirtyRectForChange(root, before, after) {
        const beforeRect = getSelectionScreenRect(root, before);
        const afterRect = getSelectionScreenRect(root, after);
        for (const rect of [beforeRect, afterRect]) {
            if (rect) addDirtyRect(root, "selection", rect);
        }

        // The header highlight follows the selection, so the old and the new active row/column strips
        // belong to the same repaint. Only the strips are dirty — the header layer is never repainted
        // as a whole for a selection change.
        for (const snapshot of [before, after]) {
            for (const rect of headerHighlightDirtyRects(root, snapshot)) {
                addDirtyRect(root, "header", rect);
            }
        }
    }

    /**
     * The column-header strip and the row-header strip a selection highlights, intersected with the
     * VISIBLE frames. A selection that extends past the viewport highlights the strips of the rows and
     * columns that are ON screen (a bounding rect between two off-screen corners would be empty, and the
     * visible part of the highlight would go stale).
     */
    function resolveHeaderStrips(bounds, rows, columns, rowHeaderWidth, columnHeaderHeight) {
        if (!bounds || !Array.isArray(rows) || !Array.isArray(columns)) return [];

        const visibleRows = rows.filter(frame => {
            const index = Number(read(frame, "Index", -1));
            return index >= bounds.minRow && index <= bounds.maxRow;
        });
        const visibleColumns = columns.filter(frame => {
            const index = Number(read(frame, "Index", -1));
            return index >= bounds.minCol && index <= bounds.maxCol;
        });

        const strips = [];
        if (visibleColumns.length > 0) {
            let left = Infinity;
            let right = -Infinity;
            for (const frame of visibleColumns) {
                left = Math.min(left, read(frame, "x", 0));
                right = Math.max(right, read(frame, "x", 0) + read(frame, "width", 0));
            }
            if (right > left) {
                strips.push({ x: left, y: 0, width: right - left, height: columnHeaderHeight });
            }
        }

        if (visibleRows.length > 0) {
            let top = Infinity;
            let bottom = -Infinity;
            for (const frame of visibleRows) {
                top = Math.min(top, read(frame, "y", 0));
                bottom = Math.max(bottom, read(frame, "y", 0) + read(frame, "height", 0));
            }
            if (bottom > top) {
                strips.push({ x: 0, y: top, width: rowHeaderWidth, height: bottom - top });
            }
        }

        return strips;
    }

    /** The dirty rects the header highlight moved off and onto, in screen space. */
    function headerHighlightDirtyRects(root, snapshot) {
        const s = getState(root);
        const model = s?.model;
        if (!model || !snapshot) return [];

        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        const layout = getVisibleLayout(root, model, width, height);
        return resolveHeaderStrips(
            selectionBounds(snapshot),
            layout.rows,
            layout.columns,
            layout.rowHeaderWidth,
            layout.columnHeaderHeight);
    }

    function getFormulaReferenceCells(root, model) {
        const s = getState(root);
        const formulaRefs = s?.sheetState?.formulaEditor?.active ? s.sheetState.formulaEditor.refs || [] : [];
        if (formulaRefs.length > 0) {
            const rowCount = read(model, "RowCount", 1000000);
            const colCount = read(model, "ColumnCount", 1000000);
            const cells = [];
            const activeTokenIndex = Number(s?.sheetState?.formulaEditor?.activeTokenIndex ?? -1);
            for (let refIndex = 0; refIndex < formulaRefs.length; refIndex++) {
                const ref = formulaRefs[refIndex];
                const startRow = Math.max(0, Math.min(rowCount - 1, ref.startRow));
                const endRow = Math.max(0, Math.min(rowCount - 1, ref.endRow));
                const startCol = Math.max(0, Math.min(colCount - 1, ref.startCol));
                const endCol = Math.max(0, Math.min(colCount - 1, ref.endCol));
                let emitted = 0;
                for (let row = startRow; row <= endRow && emitted < 5000; row++) {
                    for (let col = startCol; col <= endCol && emitted < 5000; col++) {
                        cells.push({
                            Row: row,
                            Col: col,
                            Ref: toCellRef(row, col),
                            FormulaRefColorIndex: ref.colorIndex,
                            ActiveFormulaToken: activeTokenIndex >= 0 && activeTokenIndex === refIndex
                        });
                        emitted += 1;
                    }
                }
            }
            if (s?.metrics) {
                s.metrics.formulaEditorHighlightCount = cells.length;
                s.metrics.formulaEditorReferenceCount = formulaRefs.length;
            }
            return cells;
        }

        const externalFormulaSession = getExternalFormulaSession(root);
        if (externalFormulaSession?.text) {
            const analysis = analyzeFormulaSession(
                root,
                externalFormulaSession.text,
                externalFormulaSession.selectionStart ?? 0,
                externalFormulaSession.selectionEnd ?? externalFormulaSession.selectionStart ?? 0);
            const externalRefs = analysis.referenceTokens || [];
            if (externalRefs.length > 0) {
                const rowCount = read(model, "RowCount", 1000000);
                const colCount = read(model, "ColumnCount", 1000000);
                const cells = [];
                const activeTokenIndex = Number(analysis.activeReferenceTokenIndex ?? -1);
                for (let refIndex = 0; refIndex < externalRefs.length; refIndex++) {
                    const ref = externalRefs[refIndex];
                    const startRow = Math.max(0, Math.min(rowCount - 1, ref.startRow));
                    const endRow = Math.max(0, Math.min(rowCount - 1, ref.endRow));
                    const startCol = Math.max(0, Math.min(colCount - 1, ref.startCol));
                    const endCol = Math.max(0, Math.min(colCount - 1, ref.endCol));
                    let emitted = 0;
                    for (let row = startRow; row <= endRow && emitted < 5000; row++) {
                        for (let col = startCol; col <= endCol && emitted < 5000; col++) {
                            cells.push({
                                Row: row,
                                Col: col,
                                Ref: toCellRef(row, col),
                                FormulaRefColorIndex: ref.colorIndex,
                                ActiveFormulaToken: activeTokenIndex >= 0 && activeTokenIndex === refIndex
                            });
                            emitted += 1;
                        }
                    }
                }
                if (s?.metrics) {
                    s.metrics.formulaEditorHighlightCount = cells.length;
                    s.metrics.formulaEditorReferenceCount = externalRefs.length;
                }
                return cells;
            }
        }
        const directCells = [];
        const seenDirectCells = new Set();
        const appendDirectCell = cell => {
            const colorIndex = Number(read(cell, "FormulaRefColorIndex", -1));
            if (!Number.isFinite(colorIndex) || colorIndex < 0) return;
            const row = Number(read(cell, "Row", 0)) || 0;
            const col = Number(read(cell, "Col", 0)) || 0;
            const key = cellStoreKey(row, col);
            if (seenDirectCells.has(key)) return;
            seenDirectCells.add(key);
            directCells.push(cell);
        };
        for (const cell of read(model, "Cells", []) || []) appendDirectCell(cell);
        for (const cell of s?.sheetState?.cellStore?.cells?.values?.() || []) appendDirectCell(cell);
        if (directCells.length > 0) {
            if (s?.metrics) {
                s.metrics.formulaEditorHighlightCount = directCells.length;
                s.metrics.formulaEditorReferenceCount = directCells.length;
            }
            return directCells;
        }
        if (s) {
            s.formulaReferenceSource = null;
            s.formulaReferenceRevision = 0;
            s.formulaReferenceCells = [];
            const host = root?.closest?.(".tm-spreadsheet");
            const hostSessionOwner = String(host?.__tmSpreadsheetFormulaSession?.owner || "");
            const hasLocalFormulaEditor = !!s.sheetState?.formulaEditor?.active || !!s.editor;
            if (!hasLocalFormulaEditor && hostSessionOwner === "inline") {
                clearHostFormulaSession(root, "inline");
                forceClearHostFormulaSession(root, "inline");
                removeStrayInlineEditors(root);
            } else if (!s.editor && !isHostFormulaPointMode(root)) {
                clearHostFormulaSession(root, "inline");
                clearHostFormulaSession(root, "formulaBar");
                forceClearHostFormulaSession(root, "inline");
                forceClearHostFormulaSession(root, "formulaBar");
                removeStrayInlineEditors(root);
            }
            if (s.metrics) {
                s.metrics.formulaEditorHighlightCount = 0;
                s.metrics.formulaEditorReferenceCount = 0;
            }
        }

        return [];
    }

    function updateCellSelectionFlags(model, row, col, active, selected, selectionEnd) {
        const cell = findCell(model, row, col);
        if (!cell) return;
        write(cell, "Active", !!active);
        write(cell, "Selected", !!selected);
        write(cell, "SelectionEnd", !!selectionEnd);
    }

    function updateLocalActiveCell(root, row, col, extendSelection, source) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return;
        const beforeSelection = getSelectionSnapshot(root);
        markLocalInteraction(root, source || "pointer");

        const previousActive = parseCellRef(read(model, "ActiveCellRef", "A1"));
        const selection = read(model, "Selection", {});
        const startRow = extendSelection ? read(selection, "StartRow", row) : row;
        const startCol = extendSelection ? read(selection, "StartCol", col) : col;
        setSheetSelection(root, row, col, startRow, startCol, row, col);

        write(model, "ActiveCellRef", toCellRef(row, col));
        write(selection, "StartRow", startRow);
        write(selection, "StartCol", startCol);
        write(selection, "EndRow", row);
        write(selection, "EndCol", col);

        if (previousActive.row !== row || previousActive.col !== col) {
            updateCellSelectionFlags(model, previousActive.row, previousActive.col, false, false, false);
        }
        updateCellSelectionFlags(model, row, col, true, true, true);
        addSelectionDirtyRectForChange(root, beforeSelection, getSelectionSnapshot(root));
        updateActiveCellDomProxy(root);

        requestCanvasRedraw(root, source || "selection", "selection");
        scheduleLiveRegionUpdate(root, source === "keyboard" ? 90 : defaultLiveRegionDebounceMs);
    }

    function getLocalCellFromClientPoint(root, clientX, clientY) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return null;

        const rect = root.getBoundingClientRect();
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const bodyWidth = Math.max(1, root.clientWidth - rowHeaderWidth);
        const bodyHeight = Math.max(1, root.clientHeight - columnHeaderHeight);
        const localX = Math.max(0, Math.min(bodyWidth - 1, clientX - rect.left - rowHeaderWidth));
        const localY = Math.max(0, Math.min(bodyHeight - 1, clientY - rect.top - columnHeaderHeight));
        const contentX = getLogicalScrollLeft(root) + localX;
        const contentY = getLogicalScrollTop(root) + localY;
        const layout = getLayoutState(root);
        let row;
        let col;
        if (layout) {
            row = binarySearchOffset(getRowOffsets(layout), contentY);
            col = binarySearchOffset(getColumnOffsets(layout), contentX);
            if (s.metrics) s.metrics.visibleLayoutBinarySearchCount += 2;
        } else {
            const rowCount = read(model, "RowCount", 1);
            const colCount = read(model, "ColumnCount", 1);
            row = findIndexAtContentOffset(read(model, "Rows", []), contentY, "Top", "Height", "Index", 0, Math.max(0, rowCount - 1));
            col = findIndexAtContentOffset(read(model, "Columns", []), contentX, "Left", "Width", "Index", 0, Math.max(0, colCount - 1));
        }

        return { row, col, cell: findCell(model, row, col) };
    }

    function updateDragSelectionTarget(root, clientX, clientY) {
        const selectionDrag = getSelectionDrag(root);
        if (!selectionDrag) return false;

        const hit = hitCell(root, toContentPoint(root, { clientX, clientY })) || getLocalCellFromClientPoint(root, clientX, clientY);
        if (!hit) return false;

        if (hit.row !== selectionDrag.row || hit.col !== selectionDrag.col) {
            selectionDrag.row = hit.row;
            selectionDrag.col = hit.col;
            setSelectionDrag(root, selectionDrag);
            updateLocalActiveCell(root, hit.row, hit.col, true, "pointer");
            return true;
        }

        return false;
    }

    function updateDragAutoscroll(root, clientX, clientY, pointerId) {
        const s = getState(root);
        if (!s) return;
        setDragAutoscrollPointer(root, { clientX, clientY, pointerId });
        if (s.dragAutoscrollFrame) return;
        s.dragAutoscrollFrame = requestAnimationFrame(() => runDragAutoscroll(root));
    }

    function stopDragAutoscroll(root) {
        const s = getState(root);
        if (!s) return;
        if (s.dragAutoscrollFrame) {
            cancelAnimationFrame(s.dragAutoscrollFrame);
            s.dragAutoscrollFrame = 0;
        }
        setDragAutoscrollPointer(root, null);
    }

    function runDragAutoscroll(root) {
        const s = getState(root);
        if (!s) return;
        s.dragAutoscrollFrame = 0;
        const selectionDrag = getSelectionDrag(root);
        const autoFill = getAutoFillState(root);
        const pointer = s.sheetState?.drag?.autoscrollPointer ?? s.dragAutoscrollPointer;
        if ((!selectionDrag && !autoFill?.active) || !pointer) return;

        const point = pointer;
        const model = s.model;
        const rect = root.getBoundingClientRect();
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const edge = 36;
        const maxStep = 28;
        const bodyLeft = rect.left + rowHeaderWidth;
        const bodyTop = rect.top + columnHeaderHeight;
        const bodyRight = rect.right;
        const bodyBottom = rect.bottom;

        const leftPower = Math.max(0, edge - (point.clientX - bodyLeft)) / edge;
        const rightPower = Math.max(0, edge - (bodyRight - point.clientX)) / edge;
        const upPower = Math.max(0, edge - (point.clientY - bodyTop)) / edge;
        const downPower = Math.max(0, edge - (bodyBottom - point.clientY)) / edge;
        const dx = Math.round((rightPower - leftPower) * maxStep);
        const dy = Math.round((downPower - upPower) * maxStep);
        const beforeLeft = getLogicalScrollLeft(root);
        const beforeTop = getLogicalScrollTop(root);

        if (dx || dy) {
            const scrolled = setLogicalScroll(root, beforeLeft + dx, beforeTop + dy, "pointer");
            if (selectionDrag) updateDragSelectionTarget(root, point.clientX, point.clientY);
            else if (autoFill?.active) updateAutoFillPreview(root, point.clientX, point.clientY);
            if (scrolled) {
                requestCanvasRedraw(root, "scroll", "full");
                scheduleNativeScrollSync(root, 120);
                debounceViewportSyncAfterPaint(root, false, 120);
            } else {
                requestCanvasRedraw(root, "drag-autoscroll", "selection");
            }
            if (s.metrics) s.metrics.dragAutoscrollFrames += 1;
        } else {
            if (selectionDrag) updateDragSelectionTarget(root, point.clientX, point.clientY);
            else if (autoFill?.active) updateAutoFillPreview(root, point.clientX, point.clientY);
        }

        if (getSelectionDrag(root) || getAutoFillState(root)?.active) {
            s.dragAutoscrollFrame = requestAnimationFrame(() => runDragAutoscroll(root));
        }
    }

    function sendSelection(root) {
        const s = getState(root);
        const model = s?.model;
        if (!s || !model) return;

        const snapshot = getSelectionSnapshot(root);
        if (s.metrics) s.metrics.selectionCallbackCount += 1;
        queueCommand(root, "selectionSettled", { selection: snapshot }, {
            delay: getSettledCommandDelay(root, defaultCommandLogDebounceMs)
        });
    }

    function getSettledCommandDelay(root, baseDelay) {
        const s = getState(root);
        if (!s) return baseDelay ?? defaultCommandLogDebounceMs;
        const hasPendingCellEditCommit = !!(s.cellEditCommitTimer || (s.pendingCellEditCommits && s.pendingCellEditCommits.length > 0));
        if (!hasPendingCellEditCommit) return baseDelay ?? defaultCommandLogDebounceMs;
        return Math.max(baseDelay ?? defaultCommandLogDebounceMs, defaultEditCommitBatchDebounceMs + defaultCommandLogDebounceMs);
    }

    function flushSelectionSettled(root) {
        sendSelection(root);
        flushCommandLog(root);
    }

    const pendingStateWaitLimitMs = 2000;

    // Resolves once .NET has acknowledged every command queued up to and including `target` (the last
    // id queued before a key press), or when nothing up to `target` is left to deliver. Later commands
    // are neither waited for nor flushed here — see commandLogFlushLimit. Bounded in time: a batch whose
    // invokeMethodAsync never settles (e.g. a circuit reconnect) must not wedge every later key
    // command, so after pendingStateWaitLimitMs the command runs anyway against what .NET has. In that
    // pathological case the key command can interleave in .NET with the still-awaiting
    // OnCanvasCommandLogBatch; accepted, the alternative is a keyboard that never answers again.
    // Returns false when the grid was disposed or re-initialized meanwhile.
    async function whenCommandLogAcknowledged(root, s, target) {
        const deadline = performance.now() + pendingStateWaitLimitMs;
        for (;;) {
            if (getState(root) !== s || !s.dotNet) return false;
            const ack = s.commandLogAckRevision || 0;
            if (ack >= target) return true;
            if (!s.commandLogInFlight) {
                const undelivered = (s.commandLog || []).some(command => command.id > ack && command.id <= target);
                if (!undelivered) return true;
                flushCommandLog(root, target);
                if (!s.commandLogInFlight) return true;
            }
            const remaining = deadline - performance.now();
            if (remaining <= 0) return true;
            let timer = 0;
            await Promise.race([
                Promise.resolve(s.commandLogFlight).catch(() => { }),
                new Promise(resolve => { timer = setTimeout(resolve, remaining); })
            ]);
            clearTimeout(timer);
        }
    }

    // Idempotent per barrier: a second release must never remove ANOTHER key command's barrier, even one
    // with the same target.
    function releaseCommandLogBarrier(root, s, barrier) {
        if (barrier.released) return;
        barrier.released = true;
        const barriers = s.commandLogBarriers || [];
        const index = barriers.indexOf(barrier);
        if (index >= 0) barriers.splice(index, 1);
        if (getState(root) === s && (s.commandLog || []).length > 0 && !s.commandLogTimer) {
            scheduleCommandLogFlush(root, defaultCommandLogDebounceMs);
        }
    }

    // A .NET-side command that reads the grid's selection or cell values (copy/cut into the internal
    // clipboard, Paste Special, Delete, formatting shortcuts, ...) must run against the state the user
    // saw WHEN the key was pressed. Selection changes only ride a debounced command log (35 ms, longer
    // while an edit commit is pending) and cell edits a 120 ms commit batch, while key commands used to be
    // invoked at once — so Shift+Arrow followed quickly by Ctrl+C copied the PREVIOUS selection into the
    // internal clipboard. Now: queue a still-pending selection and edits, remember the last queued id as a
    // barrier, deliver exactly the commands up to it, then invoke. Commands queued after the press (say a
    // further Shift+Arrow during the round-trip) stay behind the barrier until the key command has been
    // dispatched. Key commands of one grid are chained on DISPATCH, not on completion: each is sent as soon
    // as its own state is acknowledged and the previous one has been sent, so .NET receives them in
    // key-press order and a key command whose .NET call never returns cannot block the keys after it.
    // The returned promise is the .NET call itself.
    function invokeDotNetAfterPendingState(root, method, args, hotPath) {
        const s = getState(root);
        if (!s?.dotNet) return Promise.resolve();

        // Only a selection that has not reached the command log yet needs sending; re-sending a settled
        // one would cost a round-trip and raise a spurious ActiveCellChanged (which, among other things,
        // ends a formula-bar editing session). In formula point mode the scheduled sync is left alone.
        const hadPendingSelection = !!(s.selectionSyncFrame || s.selectionSyncTimer);
        if (hadPendingSelection && !isFormulaPointMode(root)) {
            if (s.selectionSyncFrame) {
                cancelAnimationFrame(s.selectionSyncFrame);
                s.selectionSyncFrame = 0;
            }
            if (s.selectionSyncTimer) {
                clearTimeout(s.selectionSyncTimer);
                s.selectionSyncTimer = 0;
            }
            sendSelection(root);
        }
        flushCellEditCommitQueue(root);

        const barrier = { target: s.commandLogSeq || 0, released: false };
        (s.commandLogBarriers || (s.commandLogBarriers = [])).push(barrier);
        flushCommandLog(root, barrier.target);

        // Resolves right after the .NET call has been DISPATCHED, with the call wrapped in an object so
        // the chain does not adopt (and wait for) the call's own promise.
        const dispatch = async () => {
            try {
                if (!await whenCommandLogAcknowledged(root, s, barrier.target)) return { call: undefined };
                return { call: invokeDotNet(root, method, args, hotPath) };
            } finally {
                releaseCommandLogBarrier(root, s, barrier);
            }
        };
        const dispatched = (s.pendingStateCommandChain || Promise.resolve()).then(dispatch, dispatch);
        s.pendingStateCommandChain = dispatched.catch(() => { });
        return dispatched.then(result => result.call);
    }

    function scheduleSelectionSync(root) {
        const s = getState(root);
        if (!s || !s.model || s.selectionSyncFrame) return;
        s.selectionSyncFrame = requestAnimationFrame(() => {
            s.selectionSyncFrame = 0;
            sendSelection(root);
        });
    }

    function debounceSelectionSync(root, delay) {
        const s = getState(root);
        if (!s || !s.model) return;
        if (s.selectionSyncTimer) clearTimeout(s.selectionSyncTimer);
        s.selectionSyncTimer = setTimeout(() => {
            s.selectionSyncTimer = 0;
            sendSelection(root);
        }, delay ?? 90);
    }

    function ensureCellVisibleLocal(root, row, col) {
        const started = performance.now();
        const s = getState(root);
        const model = s?.model;
        if (!model) return false;

        const rows = read(model, "Rows", []);
        const columns = read(model, "Columns", []);
        const rowFrame = getFrameByIndex(rows, row);
        const colFrame = getFrameByIndex(columns, col);
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const rowHeight = rowFrame ? read(rowFrame, "Height", 20) : 20;
        const colWidth = colFrame ? read(colFrame, "Width", 64) : 64;
        const top = rowFrame ? read(rowFrame, "Top", row * rowHeight) : row * rowHeight;
        const left = colFrame ? read(colFrame, "Left", col * colWidth) : col * colWidth;
        const bottom = top + rowHeight;
        const right = left + colWidth;
        const bodyWidth = Math.max(0, root.clientWidth - rowHeaderWidth);
        const bodyHeight = Math.max(0, root.clientHeight - columnHeaderHeight);
        let nextLeft = getLogicalScrollLeft(root);
        let nextTop = getLogicalScrollTop(root);

        if (left < nextLeft) nextLeft = left;
        else if (right > nextLeft + bodyWidth) nextLeft = right - bodyWidth;

        if (top < nextTop) nextTop = top;
        else if (bottom > nextTop + bodyHeight) nextTop = bottom - bodyHeight;

        nextLeft = Math.max(0, nextLeft);
        nextTop = Math.max(0, nextTop);
        if (setLogicalScroll(root, nextLeft, nextTop, "keyboard")) {
            if (s.metrics) {
                s.metrics.ensureCellVisibleCount += 1;
                s.metrics.ensureCellVisibleTotalMs += performance.now() - started;
            }
            return true;
        }

        if (s.metrics) {
            s.metrics.ensureCellVisibleCount += 1;
            s.metrics.ensureCellVisibleTotalMs += performance.now() - started;
        }
        return false;
    }

    function navigateLocal(root, dRow, dCol, extendSelection) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return false;

        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        const rowCount = read(model, "RowCount", active.row + 1);
        const colCount = read(model, "ColumnCount", active.col + 1);
        const row = Math.max(0, Math.min(rowCount - 1, active.row + dRow));
        const col = Math.max(0, Math.min(colCount - 1, active.col + dCol));
        if (row === active.row && col === active.col) return true;

        updateLocalActiveCell(root, row, col, extendSelection, "keyboard");
        const scrolled = ensureCellVisibleLocal(root, row, col);
        debounceSelectionSync(root, 90);
        if (scrolled) {
            requestPaint(root, "scroll", "full");
            scheduleNativeScrollSync(root, 90);
            debounceViewportSyncAfterPaint(root, false, 90);
        } else {
            notifyViewport(root, false);
        }
        return true;
    }

    function resetKeyboardRepeatState(root) {
        const s = getState(root);
        if (!s) return;
        if (s.keyboardRepeatKey && s.metrics) s.metrics.keyboardRepeatResetCount += 1;
        s.keyboardRepeatKey = "";
        s.keyboardRepeatStartedAt = 0;
        s.keyboardRepeatLastAt = 0;
        s.keyboardRepeatCount = 0;
    }

    function keyboardRepeatAccelerationEnabled(root) {
        const configured = window.tmSpreadsheetCanvas.keyboardRepeatAccelerationEnabled;
        return configured == null
            ? keyboardRepeatAccelerationEnabledDefault
            : configured !== false;
    }

    function keyboardRepeatStepForCount(count, elapsedMs) {
        if (count >= 45 || elapsedMs >= 1200) return 8;
        if (count >= 25 || elapsedMs >= 700) return 4;
        if (count >= 10 || elapsedMs >= 300) return 2;
        return 1;
    }

    function getNavigationStep(root, ev, dRow, dCol) {
        const s = getState(root);
        if (!s) return 1;

        const now = performance.now();
        const verticalArrow = ev.key === "ArrowDown" || ev.key === "ArrowUp";
        const canAccelerate = verticalArrow
            && dRow !== 0
            && dCol === 0
            && !ev.shiftKey
            && !isFormulaPointMode(root)
            && !s.editor
            && keyboardRepeatAccelerationEnabled(root);

        if (!canAccelerate || !ev.repeat) {
            resetKeyboardRepeatState(root);
            if (s.metrics) {
                s.metrics.keyboardRepeatAccelerationEnabled = keyboardRepeatAccelerationEnabled(root);
                s.metrics.keyboardRepeatLastKey = "";
                s.metrics.keyboardRepeatLastStep = 1;
            }
            return 1;
        }

        const sameSequence = s.keyboardRepeatKey === ev.key
            && now - (s.keyboardRepeatLastAt || 0) <= keyboardRepeatPauseMs;
        if (!sameSequence) {
            if (s.keyboardRepeatKey && s.metrics) s.metrics.keyboardRepeatResetCount += 1;
            s.keyboardRepeatKey = ev.key;
            s.keyboardRepeatStartedAt = now;
            s.keyboardRepeatCount = 0;
            if (s.metrics) s.metrics.keyboardRepeatSequenceCount += 1;
        }

        s.keyboardRepeatLastAt = now;
        s.keyboardRepeatCount += 1;
        const elapsed = Math.max(0, now - (s.keyboardRepeatStartedAt || now));
        const step = keyboardRepeatStepForCount(s.keyboardRepeatCount, elapsed);
        if (s.metrics) {
            s.metrics.keyboardRepeatAccelerationEnabled = true;
            s.metrics.keyboardRepeatEventCount += 1;
            s.metrics.keyboardRepeatLastKey = ev.key;
            s.metrics.keyboardRepeatLastStep = step;
            s.metrics.keyboardRepeatMaxStep = Math.max(s.metrics.keyboardRepeatMaxStep || 1, step);
            if (step > 1) s.metrics.keyboardRepeatAcceleratedEventCount += 1;
        }

        return step;
    }

    function moveLocalTo(root, row, col, extendSelection) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return false;

        const rowCount = read(model, "RowCount", row + 1);
        const colCount = read(model, "ColumnCount", col + 1);
        row = Math.max(0, Math.min(rowCount - 1, row));
        col = Math.max(0, Math.min(colCount - 1, col));
        updateLocalActiveCell(root, row, col, extendSelection, "keyboard");
        const scrolled = ensureCellVisibleLocal(root, row, col);
        scheduleSelectionSync(root);
        if (scrolled) {
            requestPaint(root, "scroll", "full");
            scheduleNativeScrollSync(root, 90);
            scheduleForcedViewportSync(root);
        } else {
            notifyViewport(root, false);
        }
        return true;
    }

    /**
     * The Ctrl/Cmd chords the canvas CLAIMS. A chord outside this list is left to the browser (so the
     * canvas never swallows a browser shortcut it does not implement), and a claimed one is
     * `preventDefault`ed before the key reaches .NET — otherwise Ctrl+D would open the browser's
     * bookmark dialog and Ctrl+R would RELOAD the page with the grid on it.
     */
    const canvasShortcutKeys = Object.freeze(["c", "v", "x", "z", "y", "b", "i", "u", "a", "1", "5", "f", "h", "0", "d", "r"]);

    function isCanvasShortcutCommand(shortcutKey, key) {
        if (canvasShortcutKeys.includes(shortcutKey)) return true;
        return key === "Home" || key === "End";
    }

    function handleNavigationKey(root, ev) {
        if (ev.altKey || ev.metaKey || ev.ctrlKey) return false;
        const s = getState(root);
        const model = s?.model;
        if (!model) return false;

        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        let handled = false;
        switch (ev.key) {
            case "ArrowUp": handled = navigateLocal(root, -getNavigationStep(root, ev, -1, 0), 0, ev.shiftKey); break;
            case "ArrowDown": handled = navigateLocal(root, getNavigationStep(root, ev, 1, 0), 0, ev.shiftKey); break;
            case "ArrowLeft": resetKeyboardRepeatState(root); handled = navigateLocal(root, 0, -1, ev.shiftKey); break;
            case "ArrowRight": resetKeyboardRepeatState(root); handled = navigateLocal(root, 0, 1, ev.shiftKey); break;
            case "Tab": resetKeyboardRepeatState(root); handled = navigateLocal(root, 0, ev.shiftKey ? -1 : 1, false); break;
            case "Home": resetKeyboardRepeatState(root); handled = moveLocalTo(root, active.row, 0, ev.shiftKey); break;
            case "End": resetKeyboardRepeatState(root); handled = moveLocalTo(root, active.row, read(model, "ColumnCount", active.col + 1) - 1, ev.shiftKey); break;
        }

        if (handled) {
            ev.preventDefault();
            ev.stopImmediatePropagation();
        }

        return handled;
    }

    function handleCommandKey(root, ev) {
        const s = getState(root);
        if (!s?.dotNet || !s.model) return false;
        if (root.dataset.readonly === "true") return false;

        const key = ev.key || "";
        const shortcutKey = key.length === 1 ? key.toLowerCase() : key;
        const isShortcut = ev.ctrlKey || ev.metaKey;
        const isShortcutCommand = isShortcut && isCanvasShortcutCommand(shortcutKey, key);
        const isEditCommand = key === "Enter" || key === "F2" || key === "Escape" || key === "Delete";
        const isTextCommand = key.length === 1 && !ev.altKey && !ev.ctrlKey && !ev.metaKey;
        if (!isShortcutCommand && !isEditCommand && !isTextCommand) return false;

        if (isJsEngine(root) && isShortcut && ["c", "x", "v"].includes(shortcutKey)) {
            // Ctrl+Shift+V (Paste Special) must reach .NET; plain copy/cut/paste use the native
            // clipboard events (onCopy/onCut/onPaste) instead.
            if (!(shortcutKey === "v" && ev.shiftKey)) {
                return false;
            }
        }

        ev.preventDefault();
        ev.stopImmediatePropagation();
        if (key === "F2" && !isFormatPainterActive(root)) {
            openLocalEditorAtActive(root);
            return true;
        }

        if (key === "Enter"
            && !isFormulaPointMode(root)
            && !isFormatPainterActive(root)) {
            openLocalEditorAtActive(root);
            return true;
        }

        if (isTextCommand && !isFormulaPointMode(root) && !isFormatPainterActive(root)) {
            openLocalEditorAtActive(root, key);
            return true;
        }

        if (s.metrics) s.metrics.keyCommandCallbackCount += 1;
        invokeDotNetAfterPendingState(root, "OnCanvasKeyCommand", [
            key,
            !!ev.shiftKey,
            !!ev.ctrlKey,
            !!ev.altKey,
            !!ev.metaKey
        ], true).catch(() => {});
        return true;
    }

    function openLocalEditorAtActive(root, initialValue) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return;

        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        openLocalEditor(root, {
            row: active.row,
            col: active.col,
            cell: findCell(model, active.row, active.col)
        }, initialValue);
    }

    function createEditorCellPatch(root, row, col, value) {
        const model = getState(root)?.model || {};
        const existing = findCell(model, row, col) || {};
        const ref = read(existing, "Ref", toCellRef(row, col));
        return {
            ...existing,
            row,
            col,
            Row: row,
            Col: col,
            ref,
            Ref: ref,
            value,
            Value: value,
            formula: String(value || "").startsWith("=") ? value : null,
            Formula: String(value || "").startsWith("=") ? value : null
        };
    }

    function getCellEditorValue(cell) {
        if (!cell) return "";
        const formula = read(cell, "Formula", null);
        if (typeof formula === "string" && formula.length > 0) return formula;
        return read(cell, "Value", "") || "";
    }

    function toCommandCellValue(value) {
        if (value == null) return "";
        return String(value);
    }

    function selectionBounds(snapshot) {
        const rowValue = Number(snapshot?.row ?? 0);
        const colValue = Number(snapshot?.col ?? 0);
        const row = Number.isFinite(rowValue) ? rowValue : 0;
        const col = Number.isFinite(colValue) ? colValue : 0;
        const startRowValue = Number(snapshot?.startRow ?? row);
        const startColValue = Number(snapshot?.startCol ?? col);
        const endRowValue = Number(snapshot?.endRow ?? row);
        const endColValue = Number(snapshot?.endCol ?? col);
        const startRow = Number.isFinite(startRowValue) ? startRowValue : row;
        const startCol = Number.isFinite(startColValue) ? startColValue : col;
        const endRow = Number.isFinite(endRowValue) ? endRowValue : row;
        const endCol = Number.isFinite(endColValue) ? endColValue : col;
        return {
            row,
            col,
            startRow,
            startCol,
            endRow,
            endCol,
            minRow: Math.min(startRow, endRow),
            maxRow: Math.max(startRow, endRow),
            minCol: Math.min(startCol, endCol),
            maxCol: Math.max(startCol, endCol)
        };
    }

    function selectionToClipboardText(root, snapshot) {
        const bounds = selectionBounds(snapshot || getSelectionSnapshot(root));
        const lines = [];
        for (let row = bounds.minRow; row <= bounds.maxRow; row++) {
            const values = [];
            for (let col = bounds.minCol; col <= bounds.maxCol; col++) {
                values.push(getCellEditorValue(findCell(getState(root)?.model, row, col)));
            }
            lines.push(values.join("\t"));
        }
        return lines.join("\n");
    }

    function buildClipboardPayload(root, snapshot, isCut) {
        const bounds = selectionBounds(snapshot || getSelectionSnapshot(root));
        const cells = [];
        for (let row = bounds.minRow; row <= bounds.maxRow; row++) {
            for (let col = bounds.minCol; col <= bounds.maxCol; col++) {
                const cell = findCell(getState(root)?.model, row, col);
                cells.push({
                    row,
                    col,
                    ref: toCellRef(row, col),
                    value: read(cell, "Value", ""),
                    formula: read(cell, "Formula", null),
                    style: read(cell, "Style", null),
                    imageUrl: read(cell, "ImageUrl", null),
                    hyperlink: read(cell, "Hyperlink", null)
                });
            }
        }

        return {
            sourceRange: `${toCellRef(bounds.minRow, bounds.minCol)}:${toCellRef(bounds.maxRow, bounds.maxCol)}`,
            isCut: !!isCut,
            cells
        };
    }

    function parseClipboardText(text) {
        const normalized = String(text || "").replace(/\r\n/g, "\n").replace(/\r/g, "\n");
        const trimmed = normalized.endsWith("\n") ? normalized.slice(0, -1) : normalized;
        if (!trimmed) return [];
        return trimmed.split("\n").map(line => line.split("\t"));
    }

    function markRangeDirty(root, startRow, startCol, endRow, endCol, layer) {
        const rect = getSelectionScreenRect(root, { row: endRow, col: endCol, startRow, startCol, endRow, endCol });
        if (rect) {
            addDirtyRect(root, layer || "content", rect);
            return true;
        }
        return false;
    }

    function adjustFormulaToken(token, dRow, dCol) {
        const match = /^(\$?)([A-Za-z]+)(\$?)(\d+)$/.exec(String(token || ""));
        if (!match) return token;
        const colAbs = match[1] === "$";
        const colLetters = match[2].toUpperCase();
        const rowAbs = match[3] === "$";
        const rowNumber = Number(match[4]) || 1;

        let nextColLetters = colLetters;
        if (!colAbs) {
            const nextCol = columnLettersToIndex(colLetters) + dCol;
            if (nextCol < 0) return "#REF!";
            nextColLetters = columnIndexToLetters(nextCol);
        }

        let nextRow = rowNumber;
        if (!rowAbs) {
            nextRow += dRow;
            if (nextRow < 1) return "#REF!";
        }

        return `${colAbs ? "$" : ""}${nextColLetters}${rowAbs ? "$" : ""}${nextRow}`;
    }

    function adjustFormulaForCopy(formula, dRow, dCol) {
        if (!formula || (!dRow && !dCol)) return formula;
        return String(formula).replace(/(\$?[A-Za-z]{1,3}\$?\d{1,7})(:(\$?[A-Za-z]{1,3}\$?\d{1,7}))?/g, match => {
            const parts = match.split(":");
            if (parts.length === 2) {
                return `${adjustFormulaToken(parts[0], dRow, dCol)}:${adjustFormulaToken(parts[1], dRow, dCol)}`;
            }
            return adjustFormulaToken(match, dRow, dCol);
        });
    }

    function buildClipboardPayloadPatches(root, payload, anchorRow, anchorCol) {
        const cells = Array.isArray(payload?.cells) ? payload.cells : [];
        if (!cells.length) return [];
        const sourceRange = String(payload.sourceRange || `${toCellRef(anchorRow, anchorCol)}:${toCellRef(anchorRow, anchorCol)}`);
        const sourceStart = parseCellRef(sourceRange.split(":")[0] || "A1");
        const dRow = anchorRow - sourceStart.row;
        const dCol = anchorCol - sourceStart.col;
        const isCut = !!payload.isCut;

        return cells.map(sourceCell => {
            const srcRow = Number(sourceCell.row ?? parseCellRef(sourceCell.ref || "A1").row) || 0;
            const srcCol = Number(sourceCell.col ?? parseCellRef(sourceCell.ref || "A1").col) || 0;
            const row = srcRow + dRow;
            const col = srcCol + dCol;
            const formula = sourceCell.formula && !isCut
                ? adjustFormulaForCopy(sourceCell.formula, row - srcRow, col - srcCol)
                : (sourceCell.formula || null);
            const value = formula ? formula : toCommandCellValue(sourceCell.value);
            return {
                Row: row,
                Col: col,
                Ref: toCellRef(row, col),
                Value: value,
                Formula: formula,
                Style: sourceCell.style || null,
                ImageUrl: sourceCell.imageUrl || null,
                Hyperlink: sourceCell.hyperlink || null
            };
        });
    }

    function buildTextPastePatches(root, rows, anchorRow, anchorCol) {
        const patches = [];
        for (let rowOffset = 0; rowOffset < rows.length; rowOffset++) {
            const values = rows[rowOffset] || [];
            for (let colOffset = 0; colOffset < values.length; colOffset++) {
                const raw = values[colOffset] ?? "";
                const row = anchorRow + rowOffset;
                const col = anchorCol + colOffset;
                patches.push({
                    Row: row,
                    Col: col,
                    Ref: toCellRef(row, col),
                    Value: raw,
                    Formula: raw.startsWith("=") ? raw : null
                });
            }
        }
        return patches;
    }

    function queueCellEditCommit(root, row, col, value) {
        const s = getState(root);
        if (!s) return;

        const commit = {
            row,
            col,
            value,
            interactionVersion: currentInteractionVersion(root)
        };
        const queue = s.pendingCellEditCommits || (s.pendingCellEditCommits = []);
        const existing = queue.find(item => item.row === row && item.col === col);
        if (existing) {
            existing.value = value;
            existing.interactionVersion = commit.interactionVersion;
        } else {
            queue.push(commit);
        }

        if (s.metrics) s.metrics.cellEditCommitQueuedCount += 1;
        if (s.cellEditCommitTimer) clearTimeout(s.cellEditCommitTimer);
        s.cellEditCommitTimer = setTimeout(() => flushCellEditCommitQueue(root), defaultEditCommitBatchDebounceMs);
    }

    function flushCellEditCommitQueue(root) {
        const s = getState(root);
        if (!s) return;
        if (s.cellEditCommitTimer) {
            clearTimeout(s.cellEditCommitTimer);
            s.cellEditCommitTimer = 0;
        }

        const queue = s.pendingCellEditCommits || [];
        if (queue.length === 0) return;
        const payload = queue.splice(0, queue.length);
        if (s.metrics) {
            s.metrics.cellEditCommitBatchCallbackCount += 1;
            s.metrics.cellEditCommitBatchItemCount += payload.length;
        }
        for (const edit of payload) {
            const type = String(edit.value || "").startsWith("=")
                ? "formulaCommitted"
                : "cellChanged";
            queueCommand(root, type, { cellEdits: [edit] }, { delay: defaultCommandLogDebounceMs });
        }
    }

    function applyPatchesLocally(root, patches, reason) {
        if (!Array.isArray(patches) || patches.length === 0) return 0;
        const changed = setStoreCells(root, patches, { suppressRedraw: true, queueRangeCommand: true });
        if (!changed) return 0;

        let minRow = Infinity;
        let minCol = Infinity;
        let maxRow = -Infinity;
        let maxCol = -Infinity;
        for (const patch of patches) {
            minRow = Math.min(minRow, read(patch, "Row", 0));
            minCol = Math.min(minCol, read(patch, "Col", 0));
            maxRow = Math.max(maxRow, read(patch, "Row", 0));
            maxCol = Math.max(maxCol, read(patch, "Col", 0));
        }

        if (!markRangeDirty(root, minRow, minCol, maxRow, maxCol, "content")) {
            requestPaint(root, reason || "paste", "full");
        } else {
            requestPaint(root, reason || "paste", "content");
        }

        debounceViewportSyncAfterPaint(root, false, 120);
        return changed;
    }

    function applyClipboardText(root, text) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return 0;
        const rows = parseClipboardText(text);
        if (!rows.length) return 0;

        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        markLocalInteraction(root, "paste");
        const patches = buildTextPastePatches(root, rows, active.row, active.col);
        return applyPatchesLocally(root, patches, "paste");
    }

    function applyClipboardPayload(root, payload) {
        const s = getState(root);
        const model = s?.model;
        if (!model) return 0;
        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        markLocalInteraction(root, "paste");
        const patches = buildClipboardPayloadPatches(root, payload, active.row, active.col);
        return applyPatchesLocally(root, patches, "paste");
    }

    function buildAutoFillPattern(values) {
        if (!values.length) return { kind: "repeat", value: "" };
        if (values.length >= 2) {
            const first = Number(values[0]);
            const second = Number(values[1]);
            if (Number.isFinite(first) && Number.isFinite(second) && String(values[0]).trim() !== "" && String(values[1]).trim() !== "") {
                const step = second - first;
                return { kind: "number", start: first, step };
            }

            const match1 = /^(.*?)(\d+)$/.exec(String(values[0] ?? ""));
            const match2 = /^(.*?)(\d+)$/.exec(String(values[1] ?? ""));
            if (match1 && match2 && match1[1] === match2[1]) {
                const n1 = Number(match1[2]);
                const n2 = Number(match2[2]);
                if (Number.isFinite(n1) && Number.isFinite(n2)) {
                    return { kind: "text-number", prefix: match1[1], start: n1, step: n2 - n1 };
                }
            }
        }

        return { kind: "repeat", value: values[0] };
    }

    function getAutoFillPatternValue(pattern, index) {
        switch (pattern?.kind) {
            case "number":
                return pattern.start + pattern.step * index;
            case "text-number":
                return `${pattern.prefix}${pattern.start + pattern.step * index}`;
            default:
                return pattern?.value ?? "";
        }
    }

    function buildAutoFillPatches(root, source, target) {
        if (!source || !target) return [];
        const sourceBounds = selectionBounds(source);
        const targetBounds = selectionBounds(target);
        const sourceRowCount = sourceBounds.maxRow - sourceBounds.minRow + 1;
        const sourceColCount = sourceBounds.maxCol - sourceBounds.minCol + 1;
        const vertical = targetBounds.maxRow > sourceBounds.maxRow || targetBounds.minRow < sourceBounds.minRow;
        const horizontal = targetBounds.maxCol > sourceBounds.maxCol || targetBounds.minCol < sourceBounds.minCol;
        const patches = [];

        if (vertical && !horizontal && sourceColCount === 1) {
            const values = [];
            for (let row = sourceBounds.minRow; row <= sourceBounds.maxRow; row++) {
                values.push(getCellEditorValue(findCell(getState(root)?.model, row, sourceBounds.minCol)));
            }
            const sourceCell = findCell(getState(root)?.model, sourceBounds.minRow, sourceBounds.minCol);
            const sourceFormula = read(sourceCell, "Formula", null);
            const pattern = buildAutoFillPattern(values);
            for (let row = targetBounds.minRow; row <= targetBounds.maxRow; row++) {
                for (let col = sourceBounds.minCol; col <= sourceBounds.maxCol; col++) {
                    if (row >= sourceBounds.minRow && row <= sourceBounds.maxRow) continue;
                    const formula = sourceFormula && sourceRowCount === 1
                        ? adjustFormulaForCopy(sourceFormula, row - sourceBounds.minRow, col - sourceBounds.minCol)
                        : null;
                    const value = formula ? formula : getAutoFillPatternValue(pattern, row - targetBounds.minRow);
                    patches.push({ Row: row, Col: col, Ref: toCellRef(row, col), Value: value, Formula: formula });
                }
            }
            return patches;
        }

        if (horizontal && !vertical && sourceRowCount === 1) {
            const values = [];
            for (let col = sourceBounds.minCol; col <= sourceBounds.maxCol; col++) {
                values.push(getCellEditorValue(findCell(getState(root)?.model, sourceBounds.minRow, col)));
            }
            const sourceCell = findCell(getState(root)?.model, sourceBounds.minRow, sourceBounds.minCol);
            const sourceFormula = read(sourceCell, "Formula", null);
            const pattern = buildAutoFillPattern(values);
            for (let col = targetBounds.minCol; col <= targetBounds.maxCol; col++) {
                for (let row = sourceBounds.minRow; row <= sourceBounds.maxRow; row++) {
                    if (col >= sourceBounds.minCol && col <= sourceBounds.maxCol) continue;
                    const formula = sourceFormula && sourceColCount === 1
                        ? adjustFormulaForCopy(sourceFormula, row - sourceBounds.minRow, col - sourceBounds.minCol)
                        : null;
                    const value = formula ? formula : getAutoFillPatternValue(pattern, col - targetBounds.minCol);
                    patches.push({ Row: row, Col: col, Ref: toCellRef(row, col), Value: value, Formula: formula });
                }
            }
            return patches;
        }

        for (let row = targetBounds.minRow; row <= targetBounds.maxRow; row++) {
            for (let col = targetBounds.minCol; col <= targetBounds.maxCol; col++) {
                if (row >= sourceBounds.minRow && row <= sourceBounds.maxRow && col >= sourceBounds.minCol && col <= sourceBounds.maxCol) {
                    continue;
                }

                const sourceRow = sourceBounds.minRow + ((row - sourceBounds.minRow) % sourceRowCount + sourceRowCount) % sourceRowCount;
                const sourceCol = sourceBounds.minCol + ((col - sourceBounds.minCol) % sourceColCount + sourceColCount) % sourceColCount;
                const sourceCell = findCell(getState(root)?.model, sourceRow, sourceCol);
                const formula = read(sourceCell, "Formula", null)
                    ? adjustFormulaForCopy(read(sourceCell, "Formula", null), row - sourceRow, col - sourceCol)
                    : null;
                const value = formula ? formula : getCellEditorValue(sourceCell);
                patches.push({
                    Row: row,
                    Col: col,
                    Ref: toCellRef(row, col),
                    Value: value,
                    Formula: formula,
                    Style: read(sourceCell, "Style", null),
                    ImageUrl: read(sourceCell, "ImageUrl", null),
                    Hyperlink: read(sourceCell, "Hyperlink", null)
                });
            }
        }

        return patches;
    }

    function beginAutoFillDrag(root, pointerId) {
        const snapshot = getSelectionSnapshot(root);
        setAutoFillState(root, {
            active: true,
            pointerId: Number(pointerId) || 0,
            source: snapshot,
            preview: null
        });
        requestPaint(root, "autofill", "selection");
    }

    function clearAutoFillDrag(root) {
        setAutoFillState(root, {
            active: false,
            pointerId: 0,
            source: null,
            preview: null
        });
        requestPaint(root, "autofill", "selection");
    }

    function computeAutoFillPreview(source, row, col) {
        const sourceBounds = selectionBounds(source);
        if (row >= sourceBounds.minRow && row <= sourceBounds.maxRow && col >= sourceBounds.minCol && col <= sourceBounds.maxCol) {
            return null;
        }

        const verticalDistance = Math.min(Math.abs(row - sourceBounds.minRow), Math.abs(row - sourceBounds.maxRow));
        const horizontalDistance = Math.min(Math.abs(col - sourceBounds.minCol), Math.abs(col - sourceBounds.maxCol));
        if (verticalDistance >= horizontalDistance) {
            return {
                row,
                col: sourceBounds.maxCol,
                startRow: Math.min(sourceBounds.minRow, row),
                startCol: sourceBounds.minCol,
                endRow: Math.max(sourceBounds.maxRow, row),
                endCol: sourceBounds.maxCol
            };
        }

        return {
            row: sourceBounds.maxRow,
            col,
            startRow: sourceBounds.minRow,
            startCol: Math.min(sourceBounds.minCol, col),
            endRow: sourceBounds.maxRow,
            endCol: Math.max(sourceBounds.maxCol, col)
        };
    }

    function updateAutoFillPreview(root, clientX, clientY) {
        const autoFill = getAutoFillState(root);
        if (!autoFill?.active || !autoFill.source) return false;
        const hit = hitCell(root, toContentPoint(root, { clientX, clientY })) || getLocalCellFromClientPoint(root, clientX, clientY);
        if (!hit) return false;
        const nextPreview = computeAutoFillPreview(autoFill.source, hit.row, hit.col);
        const previousPreview = autoFill.preview;
        if (JSON.stringify(previousPreview) === JSON.stringify(nextPreview)) return false;
        autoFill.preview = nextPreview;
        setAutoFillState(root, autoFill);
        requestPaint(root, "autofill", "selection");
        return true;
    }

    function commitAutoFill(root) {
        const autoFill = getAutoFillState(root);
        if (!autoFill?.active || !autoFill.source || !autoFill.preview) {
            clearAutoFillDrag(root);
            return 0;
        }

        markLocalInteraction(root, "pointer");
        const patches = buildAutoFillPatches(root, autoFill.source, autoFill.preview);
        const preview = autoFill.preview;
        clearAutoFillDrag(root);
        if (!patches.length) return 0;

        const changed = applyPatchesLocally(root, patches, "autofill");
        const target = selectionBounds(preview);
        const beforeSelection = getSelectionSnapshot(root);
        setSheetSelection(root, target.endRow, target.endCol, target.startRow, target.startCol, target.endRow, target.endCol);
        const model = getState(root)?.model;
        write(model, "ActiveCellRef", toCellRef(target.endRow, target.endCol));
        const selection = read(model, "Selection", {});
        write(selection, "StartRow", target.startRow);
        write(selection, "StartCol", target.startCol);
        write(selection, "EndRow", target.endRow);
        write(selection, "EndCol", target.endCol);
        write(model, "Selection", selection);
        addSelectionDirtyRectForChange(root, beforeSelection, getSelectionSnapshot(root));
        requestPaint(root, "autofill", "selection");
        debounceSelectionSync(root, 90);
        return changed;
    }

    function setFormulaEditorInactive(root) {
        const formula = getState(root)?.sheetState?.formulaEditor;
        if (!formula) return;
        formula.active = false;
        formula.text = "";
        formula.caret = 0;
        formula.selectionStart = 0;
        formula.selectionEnd = 0;
        formula.tokenStart = -1;
        formula.tokenEnd = -1;
        formula.caretTokenStart = -1;
        formula.caretTokenEnd = -1;
        formula.selectionTokenStart = -1;
        formula.selectionTokenEnd = -1;
        formula.activeTokenIndex = -1;
        formula.refs = [];
        formula.suggestions = [];
        formula.selectedSuggestionIndex = 0;
        formula.activeFunctionHint = null;
        formula.dragAnchor = null;
        formula.dragCurrent = null;
        clearHostFormulaSession(root, "inline");
        forceClearHostFormulaSession(root, "inline");
        removeStrayInlineEditors(root);
        const sheet = getState(root)?.sheetState;
        if (sheet) sheet.formulaMode = false;
        addDirtyRect(root, "selection", {
            x: 0,
            y: 0,
            width: root.clientWidth || 0,
            height: root.clientHeight || 0
        });
        renderFormulaEditorChrome(root);
        syncEditorAccessibility(root);
    }

    function updateFormulaEditorState(root, reason) {
        const s = getState(root);
        const editor = s?.editor;
        const formula = s?.sheetState?.formulaEditor;
        if (!s || !editor || !formula) return false;

        const text = editor.input?.value || "";
        const active = text.startsWith("=");
        const wasActive = !!formula.active;
        if (!active) {
            setFormulaEditorInactive(root);
            if (wasActive) requestCanvasRedraw(root, reason || "formula", "selection");
            return false;
        }

        const previousCaret = Number(formula.caret ?? 0) || 0;
        const previousSelectionStart = Number(formula.selectionStart ?? previousCaret) || 0;
        const previousSelectionEnd = Number(formula.selectionEnd ?? previousCaret) || 0;
        const previousTokenStart = formula.tokenStart ?? -1;
        const previousTokenEnd = formula.tokenEnd ?? -1;
        const analysis = analyzeFormulaSession(
            root,
            text,
            editor.input?.selectionStart ?? text.length,
            editor.input?.selectionEnd ?? editor.input?.selectionStart ?? text.length);
        const refs = analysis.referenceTokens || [];
        const token = analysis.activeReferenceToken;
        formula.active = true;
        formula.row = editor.row;
        formula.col = editor.col;
        formula.text = analysis.text || text;
        formula.caret = analysis.selectionStart;
        formula.selectionStart = analysis.selectionStart;
        formula.selectionEnd = analysis.selectionEnd;
        formula.tokenStart = token ? token.start : -1;
        formula.tokenEnd = token ? token.end : -1;
        formula.caretTokenStart = token ? token.start : -1;
        formula.caretTokenEnd = token ? token.end : -1;
        formula.selectionTokenStart = token ? token.start : -1;
        formula.selectionTokenEnd = token ? token.end : -1;
        formula.activeTokenIndex = analysis.activeReferenceTokenIndex ?? -1;
        formula.refs = refs;
        formula.suggestions = analysis.suggestions || [];
        formula.selectedSuggestionIndex = Math.max(0, Math.min(formula.suggestions.length - 1, formula.selectedSuggestionIndex || 0));
        formula.activeFunctionHint = analysis.activeFunctionHint || null;
        s.sheetState.formulaMode = true;
        setHostFormulaSession(root, {
            owner: "inline",
            cellRef: toCellRef(editor.row, editor.col),
            text: formula.text,
            selectionStart: formula.selectionStart,
            selectionEnd: formula.selectionEnd,
            isFormula: true
        });
        syncEditorAccessibility(root);
        if (s.metrics) {
            if (!wasActive) s.metrics.formulaEditorActivationCount += 1;
            s.metrics.formulaEditorReferenceParseCount += 1;
            s.metrics.formulaEditorReferenceCount = refs.length;
            if (previousSelectionStart !== formula.selectionStart
                || previousSelectionEnd !== formula.selectionEnd
                || previousTokenStart !== formula.tokenStart
                || previousTokenEnd !== formula.tokenEnd) {
                s.metrics.formulaEditorCaretMoveCount += 1;
            }
        }
        renderFormulaEditorChrome(root);
        requestCanvasRedraw(root, reason || "formula", "selection");
        return true;
    }

    function replaceFormulaReference(root, refText) {
        const s = getState(root);
        const editor = s?.editor;
        const formula = s?.sheetState?.formulaEditor;
        if (!s || !editor || !formula?.active) return false;

        const input = editor.input;
        const replacement = getFormulaRuntime()?.replaceReferenceAtSelection?.(
            input.value || "=",
            input.selectionStart ?? formula.selectionStart ?? formula.caret ?? (input.value || "=").length,
            input.selectionEnd ?? formula.selectionEnd ?? input.selectionStart ?? formula.caret ?? (input.value || "=").length,
            refText);
        if (!replacement) return false;

        input.value = replacement.value || "";
        input.setSelectionRange(Number(replacement.selectionStart) || 0, Number(replacement.selectionEnd) || Number(replacement.selectionStart) || 0);
        bumpLocalRevision(root, "formula-editor");
        updateSheetEditorValue(root);
        updateFormulaEditorState(root, "formula-reference");
        if (s.metrics) s.metrics.formulaEditorTokenReplaceCount += 1;
        return true;
    }

    function cycleFormulaAbsoluteReferenceAtCaret(root) {
        const s = getState(root);
        const editor = s?.editor;
        if (!s || !editor?.input) return false;

        const input = editor.input;
        const replacement = getFormulaRuntime()?.cycleReferenceAtSelection?.(
            input.value || "",
            input.selectionStart ?? (input.value || "").length,
            input.selectionEnd ?? input.selectionStart ?? (input.value || "").length);
        if (!replacement?.changed) return false;

        input.value = replacement.value || "";
        input.setSelectionRange(Number(replacement.selectionStart) || 0, Number(replacement.selectionEnd) || Number(replacement.selectionStart) || 0);
        bumpLocalRevision(root, "formula-editor");
        updateSheetEditorValue(root);
        updateFormulaEditorState(root, "formula-f4");
        return true;
    }

    function buildFormulaRangeRef(anchor, current) {
        const start = toCellRef(anchor.row, anchor.col);
        const end = toCellRef(current.row, current.col);
        return start === end ? start : `${start}:${end}`;
    }

    function clearExternalFormulaPicker(root) {
        const picker = getState(root)?.sheetState?.externalFormulaPicker;
        if (!picker) return;
        picker.active = false;
        picker.pointerId = 0;
        picker.anchor = null;
        picker.current = null;
        picker.startClientX = 0;
        picker.startClientY = 0;
        picker.moved = false;
        picker.lastRefText = "";
    }

    function isExternalFormulaSelfHit(root, hit) {
        const active = getState(root)?.sheetState?.activeCell;
        return !!(active && hit && active.row === hit.row && active.col === hit.col);
    }

    function beginExternalFormulaReferenceDrag(root, hit, ev) {
        const s = getState(root);
        const picker = s?.sheetState?.externalFormulaPicker;
        if (!s || !picker || !hit) return false;
        if (isExternalFormulaSelfHit(root, hit)) {
            clearExternalFormulaPicker(root);
            s.suppressClick = true;
            return false;
        }

        picker.active = true;
        picker.pointerId = Number(ev?.pointerId || 0);
        picker.anchor = { row: hit.row, col: hit.col };
        picker.current = { row: hit.row, col: hit.col };
        picker.startClientX = Number(ev?.clientX || 0);
        picker.startClientY = Number(ev?.clientY || 0);
        picker.moved = false;
        picker.lastRefText = "";
        s.suppressClick = true;
        return true;
    }

    function updateExternalFormulaReferenceDrag(root, hit, ev) {
        const s = getState(root);
        const picker = s?.sheetState?.externalFormulaPicker;
        if (!s || !picker?.active || !picker.anchor) return false;

        const dx = Math.abs(Number(ev?.clientX || 0) - Number(picker.startClientX || 0));
        const dy = Math.abs(Number(ev?.clientY || 0) - Number(picker.startClientY || 0));
        if (!picker.moved && dx + dy <= 4) return false;
        picker.moved = true;

        if (!hit) return false;
        if (picker.current && picker.current.row === hit.row && picker.current.col === hit.col) return false;

        picker.current = { row: hit.row, col: hit.col };
        const refText = buildFormulaRangeRef(picker.anchor, picker.current);
        if (!refText || refText === picker.lastRefText) return false;
        picker.lastRefText = refText;
        invokeDotNet(root, "OnCanvasFormulaReferenceUpdated", [refText], true).catch(() => {});
        return true;
    }

    function endExternalFormulaReferenceDrag(root, hit) {
        const s = getState(root);
        const picker = s?.sheetState?.externalFormulaPicker;
        if (!s || !picker?.active || !picker.anchor) return false;

        const target = hit || picker.current || picker.anchor;
        if (target && !isExternalFormulaSelfHit(root, target)) {
            const refText = buildFormulaRangeRef(picker.anchor, target);
            if (refText && refText !== picker.lastRefText) {
                picker.lastRefText = refText;
                invokeDotNet(root, "OnCanvasFormulaReferenceUpdated", [refText], true).catch(() => {});
            }
        }

        clearExternalFormulaPicker(root);
        return true;
    }

    function captureExternalFormulaOrigin(root) {
        const s = getState(root);
        const model = s?.model;
        if (!s?.sheetState || !model) return;
        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        const selection = getSelectionSnapshot(root);
        s.sheetState.externalFormulaOrigin = {
            row: active.row,
            col: active.col,
            selection
        };
    }

    function restoreExternalFormulaOrigin(root) {
        const s = getState(root);
        const model = s?.model;
        const origin = s?.sheetState?.externalFormulaOrigin;
        if (!s || !model || !origin) return false;

        const targetRow = Math.max(0, Number(origin.row) || 0);
        const targetCol = Math.max(0, Number(origin.col) || 0);
        const selection = origin.selection || {
            row: targetRow,
            col: targetCol,
            startRow: targetRow,
            startCol: targetCol,
            endRow: targetRow,
            endCol: targetCol
        };

        const beforeSelection = getSelectionSnapshot(root);
        const previousActive = parseCellRef(read(model, "ActiveCellRef", "A1"));
        setSheetSelection(
            root,
            targetRow,
            targetCol,
            Number(selection.startRow ?? targetRow),
            Number(selection.startCol ?? targetCol),
            Number(selection.endRow ?? targetRow),
            Number(selection.endCol ?? targetCol));

        write(model, "ActiveCellRef", toCellRef(targetRow, targetCol));
        if (previousActive.row !== targetRow || previousActive.col !== targetCol) {
            updateCellSelectionFlags(model, previousActive.row, previousActive.col, false, false, false);
        }
        updateCellSelectionFlags(model, targetRow, targetCol, true, true, true);
        addSelectionDirtyRectForChange(root, beforeSelection, getSelectionSnapshot(root));
        requestCanvasRedraw(root, "formula-origin-restore", "selection");
        return true;
    }

    function scheduleExternalFormulaOriginRestore(root) {
        const delays = [0, 40, 120];
        for (const delay of delays) {
            setTimeout(() => {
                if (!isFormulaPointMode(root)) return;
                restoreExternalFormulaOrigin(root);
            }, delay);
        }
    }

    function isFormulaEditorSelfHit(root, hit) {
        const editor = getState(root)?.editor;
        return !!(editor && hit && editor.row === hit.row && editor.col === hit.col);
    }

    function ignoreFormulaEditorSelfHit(root) {
        const s = getState(root);
        if (s?.metrics) s.metrics.formulaEditorIgnoredSelfClickCount += 1;
        preserveFormulaEditorFocus(root);
    }

    function preserveFormulaEditorFocus(root) {
        const s = getState(root);
        if (s?.editor?.input) {
            s.editor.input.focus({ preventScroll: true });
            setTimeout(() => updateFormulaEditorState(root, "formula-self-hit"), 0);
        }
    }

    function preserveFormulaBarFocus(root) {
        const host = root?.closest?.(".tm-spreadsheet");
        const input = host?.querySelector?.(".tm-spreadsheet-formula-bar__input");
        if (!(input instanceof HTMLInputElement)) return false;
        setTimeout(() => {
            try {
                const value = String(input.value || "");
                const start = Math.max(0, Math.min(value.length, Number(input.selectionStart ?? value.length) || 0));
                const end = Math.max(start, Math.min(value.length, Number(input.selectionEnd ?? start) || start));
                input.focus({ preventScroll: true });
                input.setSelectionRange(start, end);
            } catch {
                // Best effort only.
            }
        }, 0);
        return true;
    }

    function beginFormulaReferenceDrag(root, hit) {
        const s = getState(root);
        const formula = s?.sheetState?.formulaEditor;
        if (!s || !formula?.active || !hit) return false;
        if (isFormulaEditorSelfHit(root, hit)) {
            ignoreFormulaEditorSelfHit(root);
            s.suppressClick = true;
            return false;
        }
        const point = { row: hit.row, col: hit.col };
        formula.dragAnchor = point;
        formula.dragCurrent = point;
        const inserted = replaceFormulaReference(root, toCellRef(hit.row, hit.col));
        if (inserted && s.metrics) s.metrics.formulaEditorCellClickInsertCount += 1;
        s.suppressClick = true;
        return inserted;
    }

    function updateFormulaReferenceDrag(root, hit) {
        const s = getState(root);
        const formula = s?.sheetState?.formulaEditor;
        if (!s || !formula?.active || !formula.dragAnchor || !hit) return false;
        if (formula.dragCurrent && formula.dragCurrent.row === hit.row && formula.dragCurrent.col === hit.col) return true;
        formula.dragCurrent = { row: hit.row, col: hit.col };
        const refText = buildFormulaRangeRef(formula.dragAnchor, formula.dragCurrent);
        const updated = replaceFormulaReference(root, refText);
        if (updated && s.metrics) s.metrics.formulaEditorRangeDragCount += 1;
        return updated;
    }

    function endFormulaReferenceDrag(root) {
        const formula = getState(root)?.sheetState?.formulaEditor;
        if (!formula) return;
        formula.dragAnchor = null;
        formula.dragCurrent = null;
    }

    function isEditableKeyTarget(target) {
        if (!(target instanceof Element)) return false;
        return !!target.closest("input, textarea, select, [contenteditable=''], [contenteditable='true'], .tm-spreadsheet-canvas-grid__editor");
    }

    // Anything the spreadsheet opened OVER the grid: the context menu, a legacy in-grid popup, or a
    // core TmModal surface (every package dialog, incl. the column-width / row-height dialogs, which
    // render inside the grid root). The grid's own key and wheel listeners must not answer those:
    // Enter/Tab on a dialog button and Escape inside a dialog belong to the dialog.
    function isSpreadsheetOverlayTarget(target) {
        return target instanceof Element
            && !!target.closest(".tm-spreadsheet-context-menu, .tm-spreadsheet-resize-dialog, .tm-modal-overlay");
    }

    function removeFormulaEditorChrome(editor) {
        if (!editor?.chrome) return;
        editor.chrome.suggestions?.remove?.();
        editor.chrome.hint?.remove?.();
        editor.chrome = null;
    }

    function ensureFormulaEditorChrome(root) {
        const editor = getState(root)?.editor;
        if (!editor?.input) return null;
        if (editor.chrome) return editor.chrome;

        const suggestions = document.createElement("div");
        suggestions.className = "tm-spreadsheet-canvas-grid__formula-suggestions";
        suggestions.hidden = true;

        const hint = document.createElement("div");
        hint.className = "tm-spreadsheet-canvas-grid__formula-hint";
        hint.hidden = true;

        root.appendChild(suggestions);
        root.appendChild(hint);
        editor.chrome = { suggestions, hint };
        return editor.chrome;
    }

    function renderFormulaEditorChrome(root) {
        const s = getState(root);
        const editor = s?.editor;
        const formula = s?.sheetState?.formulaEditor;
        if (!editor?.input || !formula?.active) {
            removeFormulaEditorChrome(editor);
            return;
        }

        const chrome = ensureFormulaEditorChrome(root);
        const rect = getEditorCellRect(root, editor);
        if (!chrome || !rect || !rect.visible) {
            if (chrome) {
                chrome.suggestions.hidden = true;
                chrome.hint.hidden = true;
            }
            return;
        }

        const left = (root.scrollLeft || 0) + rect.x;
        const top = (root.scrollTop || 0) + rect.y + rect.h + 4;
        chrome.suggestions.style.left = `${left}px`;
        chrome.suggestions.style.top = `${top}px`;
        chrome.hint.style.left = `${left}px`;
        chrome.hint.style.top = `${top}px`;
        chrome.suggestions.style.maxWidth = `${Math.max(220, rect.w + 140)}px`;
        chrome.hint.style.maxWidth = `${Math.max(220, rect.w + 180)}px`;

        const suggestions = Array.isArray(formula.suggestions) ? formula.suggestions : [];
        if (suggestions.length > 0) {
            chrome.suggestions.hidden = false;
            chrome.hint.hidden = true;
            chrome.suggestions.innerHTML = "";
            suggestions.forEach((suggestion, index) => {
                const button = document.createElement("button");
                button.type = "button";
                button.className = `tm-spreadsheet-canvas-grid__formula-suggestion${index === (formula.selectedSuggestionIndex || 0) ? " tm-spreadsheet-canvas-grid__formula-suggestion--selected" : ""}`;
                button.innerHTML = `<span class="tm-spreadsheet-canvas-grid__formula-suggestion-name">${suggestion.name || ""}</span><span class="tm-spreadsheet-canvas-grid__formula-suggestion-signature">${suggestion.signature || ""}</span>`;
                button.addEventListener("mousedown", ev => ev.preventDefault());
                button.addEventListener("click", ev => {
                    ev.preventDefault();
                    editor.input.focus({ preventScroll: true });
                    acceptFormulaEditorSuggestion(root, index);
                });
                chrome.suggestions.appendChild(button);
            });
        } else {
            chrome.suggestions.hidden = true;
            chrome.suggestions.innerHTML = "";
            const hint = formula.activeFunctionHint;
            if (hint?.function) {
                const fn = hint.function;
                const args = Array.isArray(fn.arguments) ? fn.arguments : [];
                chrome.hint.hidden = false;
                chrome.hint.innerHTML = "";

                const name = document.createElement("div");
                name.className = "tm-spreadsheet-canvas-grid__formula-hint-name";
                name.textContent = fn.name || "";
                chrome.hint.appendChild(name);

                const signature = document.createElement("div");
                signature.className = "tm-spreadsheet-canvas-grid__formula-hint-signature";
                signature.append(`${fn.name || ""}(`);
                args.forEach((arg, index) => {
                    if (index > 0) signature.append(", ");
                    const span = document.createElement("span");
                    span.className = index === (hint.activeArgumentIndex || 0)
                        ? "tm-spreadsheet-canvas-grid__formula-hint-arg tm-spreadsheet-canvas-grid__formula-hint-arg--active"
                        : "tm-spreadsheet-canvas-grid__formula-hint-arg";
                    span.textContent = arg;
                    signature.appendChild(span);
                });
                signature.append(")");
                chrome.hint.appendChild(signature);

                const summary = document.createElement("div");
                summary.className = "tm-spreadsheet-canvas-grid__formula-hint-summary";
                summary.textContent = fn.summary || "";
                chrome.hint.appendChild(summary);
            } else {
                chrome.hint.hidden = true;
                chrome.hint.innerHTML = "";
            }
        }
    }

    function moveFormulaEditorSuggestionSelection(root, delta) {
        const formula = getState(root)?.sheetState?.formulaEditor;
        if (!formula?.active || !Array.isArray(formula.suggestions) || formula.suggestions.length === 0) return false;
        let nextIndex = Number(formula.selectedSuggestionIndex || 0) + delta;
        if (nextIndex < 0) nextIndex = formula.suggestions.length - 1;
        else if (nextIndex >= formula.suggestions.length) nextIndex = 0;
        formula.selectedSuggestionIndex = nextIndex;
        renderFormulaEditorChrome(root);
        return true;
    }

    function acceptFormulaEditorSuggestion(root, index) {
        const s = getState(root);
        const editor = s?.editor;
        const formula = s?.sheetState?.formulaEditor;
        if (!editor?.input || !formula?.active || !Array.isArray(formula.suggestions) || formula.suggestions.length === 0) return false;
        const suggestion = formula.suggestions[Math.max(0, Math.min(formula.suggestions.length - 1, index))];
        if (!suggestion?.name) return false;
        const replacement = getFormulaRuntime()?.acceptFunctionSuggestion?.(
            editor.input.value || "=",
            editor.input.selectionStart ?? formula.selectionStart ?? 0,
            editor.input.selectionEnd ?? formula.selectionEnd ?? editor.input.selectionStart ?? 0,
            suggestion.name);
        if (!replacement) return false;
        editor.input.value = replacement.value || "";
        editor.input.setSelectionRange(Number(replacement.selectionStart) || 0, Number(replacement.selectionEnd) || Number(replacement.selectionStart) || 0);
        bumpLocalRevision(root, "formula-editor");
        updateSheetEditorValue(root);
        updateFormulaEditorState(root, "formula-suggestion");
        editor.input.focus({ preventScroll: true });
        return true;
    }

    function closeLocalEditor(root, commit) {
        const s = getState(root);
        const editor = s?.editor;
        if (!s || !editor) return;

        const input = editor.input;
        if (!input || input.__tmClosing) return;
        input.__tmClosing = true;
        if (s.metrics) s.metrics.editorRemoveAttemptCount += 1;
        if (!commit && s.metrics) s.metrics.editorCancelCount += 1;
        if (commit && s.model) {
            bumpLocalRevision(root, "editor");
            updateSheetEditorValue(root);
            const value = input.value;
            const cell = findCell(s.model, editor.row, editor.col);
            const previousValue = editor.initialValue ?? (cell ? (read(cell, "Value", "") || "") : "");
            const changed = value !== previousValue;
            if (changed) {
                const patch = createEditorCellPatch(root, editor.row, editor.col, value);
                if (cell) write(cell, "Value", value);
                setStoreCells(root, [patch], { suppressRedraw: true });
                invalidateCellSnapshot(root, editor.row, editor.col);
                addDirtyRect(root, "content", getCellScreenRect(root, editor.row, editor.col));
                requestCanvasRedraw(root, "edit", "content");
                queueCellEditCommit(root, editor.row, editor.col, value);
                if (s.metrics) s.metrics.editorLocalCommitCount += 1;
            }
        }

        removeFormulaEditorChrome(editor);
        input.remove();
        if (s.metrics) {
            s.metrics.editorRemoveCompleteCount += 1;
            s.metrics.editorLastDomCountAfterRemove = root.querySelectorAll?.(".tm-spreadsheet-canvas-grid__editor")?.length || 0;
        }
        setSheetEditor(root, null);
        setFormulaEditorInactive(root);
        syncAccessibilityState(root, commit ? "immediate" : false);
        root.focus?.({ preventScroll: true });
        setTimeout(() => finalizeInlineEditorCleanup(root), 0);
    }

    function handleHyperlinkClick(root, hyperlink) {
        if (!hyperlink) return;
        const kind = hyperlink.kind ?? hyperlink.Kind ?? 0;
        const target = hyperlink.target ?? hyperlink.Target ?? "";
        if (!target) return;

        // 0 = Web, 1 = Email, 2 = InternalRef, 3 = NamedRange
        if (kind === 0 || kind === "Web") {
            window.open(target, "_blank");
        } else if (kind === 1 || kind === "Email") {
            const subject = hyperlink.emailSubject ?? hyperlink.EmailSubject ?? "";
            const uri = subject ? "mailto:" + encodeURIComponent(target) + "?subject=" + encodeURIComponent(subject) : "mailto:" + encodeURIComponent(target);
            window.location.href = uri;
        } else if (kind === 2 || kind === "InternalRef" || kind === 3 || kind === "NamedRange") {
            invokeDotNet(root, "HandleHyperlinkClick", [kind, target], true).catch(() => {});
        }
    }

    function finalizeInlineEditorCleanup(root) {
        const s = getState(root);
        if (!s) return;
        if (s.editor || s.sheetState?.formulaEditor?.active) return;

        clearHostFormulaSession(root, "inline");
        forceClearHostFormulaSession(root, "inline");

        let removedStrayEditor = false;
        removedStrayEditor = removeStrayInlineEditors(root);

        if (removedStrayEditor) {
            addDirtyRect(root, "selection", {
                x: 0,
                y: 0,
                width: root.clientWidth || 0,
                height: root.clientHeight || 0
            });
            requestCanvasRedraw(root, "inline-editor-cleanup", "selection");
        }

        syncEditorAccessibility(root);
        syncAccessibilityState(root, false);
    }

    function scheduleInlineEditorCleanup(root, attempt) {
        const s = getState(root);
        if (!s) return;
        const nextAttempt = Number(attempt || 0);
        if (s.inlineEditorCleanupTimer) {
            clearTimeout(s.inlineEditorCleanupTimer);
            s.inlineEditorCleanupTimer = 0;
        }

        s.inlineEditorCleanupTimer = setTimeout(() => {
            s.inlineEditorCleanupTimer = 0;
            const state = getState(root);
            if (!state) return;
            if (state.editor || state.sheetState?.formulaEditor?.active) {
                if (nextAttempt < 8) {
                    scheduleInlineEditorCleanup(root, nextAttempt + 1);
                }
                return;
            }

            finalizeInlineEditorCleanup(root);
        }, nextAttempt === 0 ? 30 : 40);
    }

    function removeStrayInlineEditors(root) {
        const s = getState(root);
        const activeInput = s?.editor?.input || null;
        let removed = false;
        const editors = root?.querySelectorAll?.(".tm-spreadsheet-canvas-grid__editor") || [];
        for (const input of editors) {
            if (input && input !== activeInput) {
                input.remove();
                removed = true;
            }
        }
        return removed;
    }

    function commitLocalEditorAndNavigate(root, dRow, dCol, extendSelection) {
        closeLocalEditor(root, true);
        clearHostFormulaSession(root, "inline");
        forceClearHostFormulaSession(root, "inline");
        removeStrayInlineEditors(root);
        navigateLocal(root, dRow, dCol, !!extendSelection);
        scheduleInlineEditorCleanup(root, 0);
    }

    function getEditorCellRect(root, editor) {
        const s = getState(root);
        const model = s?.model;
        if (!model || !editor) return null;

        const layout = getVisibleLayout(root, model, root.clientWidth || read(model, "ViewportWidth", 1), root.clientHeight || read(model, "ViewportHeight", 1));
        const row = getFrameByIndex(layout.rows, editor.row);
        const col = getFrameByIndex(layout.columns, editor.col);
        if (!row || !col) return null;

        const x = read(col, "x", screenX(model, col));
        const y = read(row, "y", screenY(model, row));
        const w = read(col, "Width", 64);
        const h = read(row, "Height", 20);
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const visible = w > 0
            && h > 0
            && x + w >= rowHeaderWidth
            && y + h >= columnHeaderHeight
            && x <= (root.clientWidth || 0)
            && y <= (root.clientHeight || 0);

        return { x, y, w, h, visible };
    }

    function updateLocalEditorPosition(root) {
        const s = getState(root);
        const editor = s?.editor;
        if (!editor) return;
        const started = performance.now();

        const rect = getEditorCellRect(root, editor);
        if (!rect || !rect.visible) {
            editor.suppressBlur = true;
            editor.input.style.visibility = "hidden";
            renderFormulaEditorChrome(root);
            if (s.metrics) {
                s.metrics.editorLayerUpdateCount += 1;
                s.metrics.lastEditorLayerMs = performance.now() - started;
            }
            if (s.sheetState?.formulaEditor?.active) {
                return;
            }
            setTimeout(() => {
                if (editor) editor.suppressBlur = false;
            }, 0);
            return;
        }

        editor.suppressBlur = false;
        editor.input.style.display = "";
        editor.input.style.visibility = "";
        editor.input.style.left = `${(root.scrollLeft || 0) + rect.x}px`;
        editor.input.style.top = `${(root.scrollTop || 0) + rect.y}px`;
        editor.input.style.width = `${Math.max(16, rect.w)}px`;
        editor.input.style.height = `${Math.max(8, rect.h)}px`;
        renderFormulaEditorChrome(root);
        if (s.metrics) {
            s.metrics.editorLayerUpdateCount += 1;
            s.metrics.lastEditorLayerMs = performance.now() - started;
        }
    }

    function openLocalEditor(root, hit, initialValue) {
        const s = getState(root);
        const model = s?.model;
        if (!s || !model || !hit) return;

        closeLocalEditor(root, false);
        bumpLocalRevision(root, "editor");

        const cell = hit.cell || findCell(model, hit.row, hit.col);
        const originalValue = getCellEditorValue(cell);
        const cellRef = toCellRef(hit.row, hit.col);
        const hostSession = getHostFormulaSession(root);
        const restoredSession = hostSession && String(hostSession.cellRef || "").toUpperCase() === cellRef.toUpperCase()
            ? hostSession
            : null;
        const value = initialValue ?? restoredSession?.text ?? originalValue;

        const input = document.createElement("input");
        input.className = "tm-spreadsheet-canvas-grid__editor";
        input.value = value;
        // Phase 7: a finger gets the right virtual keyboard (decimal for a numeric cell, text for a
        // date/text cell) — set on every editor, a hardware keyboard simply ignores it.
        applyCellInputMode(input, cell);
        input.addEventListener("click", ev => ev.stopPropagation());
        input.addEventListener("dblclick", ev => ev.stopPropagation());
        input.addEventListener("keydown", ev => {
            const formulaState = getState(root)?.sheetState?.formulaEditor;
            const formulaEditing = !!formulaState?.active;
            const hasSuggestions = !!(formulaState?.suggestions?.length);
            if (formulaEditing && hasSuggestions && ev.key === "ArrowDown") {
                ev.preventDefault();
                ev.stopPropagation();
                moveFormulaEditorSuggestionSelection(root, 1);
                return;
            }
            if (formulaEditing && hasSuggestions && ev.key === "ArrowUp") {
                ev.preventDefault();
                ev.stopPropagation();
                moveFormulaEditorSuggestionSelection(root, -1);
                return;
            }
            if (formulaEditing && hasSuggestions && ev.key === "Enter") {
                ev.preventDefault();
                ev.stopPropagation();
                acceptFormulaEditorSuggestion(root, formulaState.selectedSuggestionIndex || 0);
                return;
            }
            if (ev.key === "Enter") {
                ev.preventDefault();
                ev.stopPropagation();
                commitLocalEditorAndNavigate(root, ev.shiftKey ? -1 : 1, 0, false);
            } else if (ev.key === "Tab") {
                ev.preventDefault();
                ev.stopPropagation();
                commitLocalEditorAndNavigate(root, 0, ev.shiftKey ? -1 : 1, false);
            } else if (ev.key === "ArrowUp") {
                if (formulaEditing) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    setTimeout(() => updateFormulaEditorState(root, "formula-arrow"), 0);
                    return;
                }
                ev.preventDefault();
                ev.stopPropagation();
                commitLocalEditorAndNavigate(root, -1, 0, false);
            } else if (ev.key === "ArrowDown") {
                if (formulaEditing) {
                    ev.preventDefault();
                    ev.stopPropagation();
                    setTimeout(() => updateFormulaEditorState(root, "formula-arrow"), 0);
                    return;
                }
                ev.preventDefault();
                ev.stopPropagation();
                commitLocalEditorAndNavigate(root, 1, 0, false);
            } else if (ev.key === "ArrowLeft") {
                if (formulaEditing) {
                    if (getState(root)?.metrics) getState(root).metrics.formulaEditorArrowCaretCount += 1;
                    ev.stopPropagation();
                    setTimeout(() => updateFormulaEditorState(root, "formula-arrow"), 0);
                    return;
                }
                ev.preventDefault();
                ev.stopPropagation();
                commitLocalEditorAndNavigate(root, 0, -1, false);
            } else if (ev.key === "ArrowRight") {
                if (formulaEditing) {
                    if (getState(root)?.metrics) getState(root).metrics.formulaEditorArrowCaretCount += 1;
                    ev.stopPropagation();
                    setTimeout(() => updateFormulaEditorState(root, "formula-arrow"), 0);
                    return;
                }
                ev.preventDefault();
                ev.stopPropagation();
                commitLocalEditorAndNavigate(root, 0, 1, false);
            } else if (formulaEditing && (ev.key === "Home" || ev.key === "End" || ((ev.ctrlKey || ev.metaKey) && (ev.key === "ArrowLeft" || ev.key === "ArrowRight")))) {
                ev.stopPropagation();
                setTimeout(() => updateFormulaEditorState(root, "formula-arrow"), 0);
            } else if (formulaEditing && (ev.key === "PageUp" || ev.key === "PageDown")) {
                ev.preventDefault();
                ev.stopPropagation();
                setTimeout(() => updateFormulaEditorState(root, "formula-page"), 0);
            } else if (ev.key === "Escape") {
                ev.preventDefault();
                ev.stopPropagation();
                closeLocalEditor(root, false);
            } else if (ev.key === "F4") {
                if (!cycleFormulaAbsoluteReferenceAtCaret(root)) return;
                ev.preventDefault();
                ev.stopPropagation();
            } else {
                bumpLocalRevision(root, "editor");
                ev.stopPropagation();
                setTimeout(() => updateFormulaEditorState(root, "formula-key"), 0);
            }
        });
        input.addEventListener("input", () => {
            bumpLocalRevision(root, "editor");
            updateSheetEditorValue(root);
            updateFormulaEditorState(root, "formula-input");
        });
        input.addEventListener("keyup", () => updateFormulaEditorState(root, "formula-caret"));
        input.addEventListener("click", () => updateFormulaEditorState(root, "formula-caret"));
        input.addEventListener("select", () => updateFormulaEditorState(root, "formula-select"));
        input.addEventListener("dblclick", () => updateFormulaEditorState(root, "formula-dblclick"));
        input.addEventListener("blur", () => {
            if (s.editor?.suppressBlur) return;
            closeLocalEditor(root, true);
        });

        root.appendChild(input);
        setSheetEditor(root, { input, row: hit.row, col: hit.col, initialValue: originalValue });
        if (s.metrics) s.metrics.editorOpenCount += 1;
        updateFormulaEditorState(root, "formula-open");
        updateLocalEditorPosition(root);
        syncEditorAccessibility(root);
        syncAccessibilityState(root, false);
        input.focus({ preventScroll: true });
        if (restoredSession) {
            const selectionStart = Math.max(0, Math.min(input.value.length, Number(restoredSession.selectionStart) || 0));
            const selectionEnd = Math.max(0, Math.min(input.value.length, Number(restoredSession.selectionEnd) || selectionStart));
            input.setSelectionRange(selectionStart, selectionEnd);
        } else if (initialValue === undefined || initialValue === null) {
            input.select();
        } else {
            input.setSelectionRange(input.value.length, input.value.length);
        }
        updateFormulaEditorState(root, "formula-open-restore");
    }

    function toCellRef(row, col) {
        let n = col + 1;
        let letters = "";
        while (n > 0) {
            const rem = (n - 1) % 26;
            letters = String.fromCharCode(65 + rem) + letters;
            n = Math.floor((n - 1) / 26);
        }
        return letters + String(row + 1);
    }

    // ── Touch gesture sessions (phase 7) ──────────────────────────────────────────────────────
    // The single-pointer gesture comes from the shared core helper `pointer-drag.js` (threshold,
    // long-press, pointer capture, cancel paths) and the two-finger zoom from `pointer-gesture.js`
    // `attachPinch` — this file owns no drag and no pinch of its own (docs/touch-interaction.md).
    //
    // `document.currentScript` is only set while this classic script evaluates, so the helper URLs
    // are resolved NOW, next to the package's own _content folder, exactly like scheduler.js and
    // wireframe-designer.js do. A bare '_content/...' specifier is not a module specifier.
    const tmSpreadsheetHelperUrls = (function () {
        try {
            const script = document.currentScript;
            if (script && script.src) {
                return {
                    drag: new URL("../../Tempo.Blazor/js/pointer-drag.js", script.src).href,
                    gesture: new URL("../../Tempo.Blazor/js/pointer-gesture.js", script.src).href
                };
            }
        } catch (e) { /* fall through to the document-relative layout */ }

        let base = "";
        try { base = document.baseURI || ""; } catch (e) { /* no document (a node/vm host) */ }
        const resolve = path => {
            try { return new URL(path, base).href; } catch (e) { return path; }
        };
        return {
            drag: resolve("_content/Tempo.Blazor/js/pointer-drag.js"),
            gesture: resolve("_content/Tempo.Blazor/js/pointer-gesture.js")
        };
    })();

    /** The `data-touch-handle` a press landed on (the DOM handle layer painted over the selection). */
    function readTouchHandleTarget(ev) {
        const handle = ev?.target?.closest?.("[data-touch-handle]");
        if (!handle) return null;
        return String(handle.getAttribute("data-touch-handle") || "");
    }

    /**
     * The ORIGIN of a touch press — the input the pure drag recognizer needs. The order matters: the
     * header filter button and the resize edge are checked by COORDINATES (their DOM overlay, if any,
     * is transparent), and the fill handle wins over the selection markers it shares a corner with.
     */
    function classifyTouchOrigin(root, ev) {
        if (!ev) return "empty";
        if (isSpreadsheetOverlayTarget(ev.target)) return "overlay";
        const s = getState(root);
        if (s?.editor?.input && (ev.target === s.editor.input || s.editor.input.contains?.(ev.target))) return "editor";
        if (hitFilterButton(root, ev)) return "filter-button";
        if (hitValidationDropButton(root, ev)) return "validation-drop";
        if (hitResize(root, ev)) return "resize";
        if (hitAutoFillHandle(root, ev)) return "auto-fill";
        // The DOM fill box belongs to the fill gesture, even when its centre sits a few px outside the
        // painted square's hit box (the box is finger-sized, the painted handle is 6-8 px).
        if (readTouchHandleTarget(ev) === "fill") return "auto-fill";
        const hit = hitCell(root, toContentPoint(root, ev));
        return hit ? "cell" : "header";
    }

    /** The window a long-press's release-click stays swallowed (the diagram editor's 350 ms + margin). */
    const touchReleaseClickGuardMs = 400;

    /**
     * Opens the cell context menu at the finger (the long-press action).
     *
     * The finger is still DOWN when the menu appears, so the compatibility `click` of the lift would
     * land on the freshly opened menu's overlay (or on one of its items) and close/activate it
     * immediately. The guard armed here swallows document-level clicks for a short window after the
     * release; it expires on its own, so the next tap works normally.
     */
    function openTouchContextMenu(root, info) {
        const s = getState(root);
        const ev = info?.event;
        if (!s || !ev) return;
        s.suppressClick = true;
        // The guard is (re-)armed on the LIFT: a user may hold for seconds, and the click that must not
        // reach the menu is the one the release produces, not the one the press could have produced.
        s.touchMenuHold = true;
        armTouchReleaseClickGuard(root);
        const point = toContentPoint(root, { clientX: ev.clientX, clientY: ev.clientY });
        // `anchor` = the gesture kind (a finger-sized menu offsets itself from the finger) and the viewport
        // height lets the menu flip above the press point when there is no room below it.
        const viewportHeight = Math.max(0, Number(window.innerHeight) || 0);
        invokeDotNet(root, "OnCanvasContextMenu",
            [point.x, point.y, ev.clientX, ev.clientY, "touch", viewportHeight], true).catch(() => {});
    }

    function armTouchReleaseClickGuard(root) {
        if (typeof document === "undefined") return;
        const s = getState(root);
        if (!s) return;
        disarmTouchReleaseClickGuard(root);
        const swallow = event => {
            event?.preventDefault?.();
            event?.stopPropagation?.();
        };
        try {
            document.addEventListener("click", swallow, true);
            // A long-press may also synthesize the browser's context menu on the lift; the menu that
            // just opened at the finger must not be closed by that either.
            document.addEventListener("contextmenu", swallow, true);
        } catch (e) { return; }
        const timer = setTimeout(() => disarmTouchReleaseClickGuard(root), touchReleaseClickGuardMs);
        s.touchClickGuard = { swallow, timer };
    }

    function disarmTouchReleaseClickGuard(root) {
        const s = getState(root);
        const guard = s?.touchClickGuard;
        if (!s || !guard) return;
        s.touchClickGuard = null;
        // The guard also armed the canvas' own single-click swallow; releasing one must release both,
        // or the NEXT legitimate click (on a cell) would be eaten.
        s.suppressClick = false;
        try { clearTimeout(guard.timer); } catch (e) { /* already fired */ }
        try { document.removeEventListener("click", guard.swallow, true); } catch (e) { /* no document */ }
        try { document.removeEventListener("contextmenu", guard.swallow, true); } catch (e) { /* no document */ }
    }

    /** The typed virtual keyboard for the cell under the finger (double tap → edit). */
    function applyCellInputMode(input, cell) {
        if (!input || !input.setAttribute) return;
        const mode = resolveCellInputMode(cell);
        try {
            input.setAttribute("inputmode", mode);
            input.setAttribute("autocapitalize", "off");
            input.setAttribute("autocomplete", "off");
            input.spellcheck = false;
        } catch (e) { /* a host input stub without attributes */ }
    }

    /**
     * A touch TAP: select the cell, or — inside the double-tap window — open the editor with the
     * virtual keyboard the cell type asks for. The compatibility click that follows is swallowed, so
     * the mouse path and the touch path can never both act on one press.
     */
    function handleTouchTap(root, info) {
        const s = getState(root);
        const ev = info?.event;
        if (!s || !ev) return;
        const decision = resolveTouchTap({
            pointerType: info.pointerType,
            now: performance.now(),
            x: ev.clientX,
            y: ev.clientY,
            previous: s.touchTapCandidate
        });
        s.touchTapCandidate = decision.next;
        if (decision.action !== "select-cell" && decision.action !== "start-edit") return;

        s.suppressClick = true;
        s.lastPointerButton = 0;
        if (decision.action === "select-cell") {
            if (!getSelectionDrag(root) && !getAutoFillState(root)?.active) {
                activateCanvasPointer(root, ev);
            }
            return;
        }

        const hit = hitCell(root, toContentPoint(root, ev));
        if (!hit) return;
        updateLocalActiveCell(root, hit.row, hit.col, false, "pointer");
        flushSelectionSettled(root);
        if (root.dataset.readonly === "true") return;
        openLocalEditor(root, hit);
        const editor = getState(root)?.editor;
        applyCellInputMode(editor?.input, hit.cell || findCell(getState(root)?.model, hit.row, hit.col));
        editor?.input?.focus?.({ preventScroll: true });
    }

    /** Positions the DOM touch-handle layer over the painted selection (grid coordinates). */
    function syncTouchHandles(root, model) {
        const s = getState(root);
        if (!s || !model) return;
        let layer = s.touchHandles;
        if (!layer || layer.isConnected === false) {
            layer = s.touchHandles = root.querySelector?.("[data-touch-handles]") || null;
        }
        if (!layer) return;

        const coarse = read(model, "PointerCoarse", false) === true || isCoarsePointer();
        const rect = getSelectionScreenRect(root, getSelectionSnapshot(root));
        const rects = resolveTouchHandleRects(rect
            ? { x: rect.x, y: rect.y, width: rect.width, height: rect.height, coarse }
            : { coarse });
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const width = Math.max(1, root.clientWidth || 1);
        const height = Math.max(1, root.clientHeight || 1);
        const bounds = {
            x: rowHeaderWidth,
            y: columnHeaderHeight,
            width: Math.max(0, width - rowHeaderWidth),
            height: Math.max(0, height - columnHeaderHeight)
        };

        let visible = false;
        for (const key of ["start", "fill", "end"]) {
            const element = layer.querySelector?.(`[data-touch-handle="${key}"]`);
            if (!element) continue;
            // Clip rather than clamp: shifting a drop into the body would put it ON the selection.
            const box = clipTouchHandleRect({ rect: rects[key], bounds, minSize: 16 });
            if (!box) {
                element.hidden = true;
                continue;
            }
            element.hidden = false;
            element.style.left = `${Math.round(box.x)}px`;
            element.style.top = `${Math.round(box.y)}px`;
            element.style.width = `${Math.round(box.width)}px`;
            element.style.height = `${Math.round(box.height)}px`;
            visible = true;
        }
        layer.dataset.touchHandlesVisible = visible ? "true" : "false";
    }

    /**
     * Attaches the phase-7 touch sessions to a registered grid: one `pointer-drag.js` session for
     * tap / double tap / long-press, one `pointer-gesture.js` pinch for the zoom. Both helpers load
     * through a dynamic import; a failed load leaves the grid on its mouse path and logs once.
     */
    function attachTouchGestures(root) {
        const s = getState(root);
        if (!s || s.touch) return;
        const urls = tmSpreadsheetHelperUrls;
        if (!urls) return;

        s.touch = {
            ready: false,
            detach() {
                try { this.drag?.detach?.(); } catch (e) { /* already gone */ }
                try { this.pinch?.detach?.(); } catch (e) { /* already gone */ }
                this.drag = null;
                this.pinch = null;
            }
        };
        const session = s.touch;

        Promise.all([import(urls.drag), import(urls.gesture)])
            .then(([dragHelper, gestureHelper]) => {
                const state = getState(root);
                if (!state || state.touch !== session) return;   // disposed while loading

                const touchDrag = dragHelper.attach(root, {
                    pointerTypes: ["touch"],
                    longPressMs: touchLongPressMs,
                    threshold: touchDragThresholdPx,
                    // CSS owns touch-action: pan-x pan-y on the grid — the finger must still scroll.
                    touchAction: null,
                    captureOn: "press",
                    // The tap path arms the single swallow itself (a handled tap must not repeat as a
                    // compatibility click); a real drag is never followed by a click we want.
                    suppressClick: false,
                    dragClass: null,
                    onPress(info) {
                        const origin = classifyTouchOrigin(root, info.event);
                        const decision = resolveTouchDragAction({ pointerType: info.pointerType, origin });
                        // Only the bare cell surface takes taps from this session. Everything else is
                        // vetoed so its own path (fill drag, resize, filter button, editor) stays the
                        // only one — and the browser keeps handling headers natively.
                        return decision.action === "native-pan" && origin === "cell";
                    },
                    onStart(info) {
                        if (info.origin === "long-press") {
                            openTouchContextMenu(root, info);
                            return;
                        }
                        // A threshold drag on the cell surface is the browser's pan: abort the session
                        // so pointer-drag stops and nothing is prevented.
                        return false;
                    },
                    onCancel() {
                        const state2 = getState(root);
                        if (state2) state2.touchTapCandidate = null;
                    },
                    onRelease(info) {
                        const state2 = getState(root);
                        if (state2?.touchMenuHold) {
                            // The hold opened the cell menu: swallow what the LIFT produces.
                            state2.touchMenuHold = false;
                            armTouchReleaseClickGuard(root);
                        }
                        if (info.outcome !== "tap") return;
                        handleTouchTap(root, info);
                    }
                });

                const pinch = gestureHelper.attachPinch(root, {
                    pointerTypes: ["touch"],
                    // The canvas already owns ctrl+wheel zoom (OnCanvasZoomDelta); the pinch helper
                    // must not bind a second wheel listener over it.
                    wheel: false,
                    touchAction: null,
                    // A lone finger belongs to the native scroll and to the drag session above.
                    captureSinglePointer: false,
                    onStart(info) {
                        const state2 = getState(root);
                        if (!state2) return;
                        touchDrag.cancel("pinch");
                        cancelCanvasGesturesForPinch(root);
                        state2.touchTapCandidate = null;
                        state2.pinchStartZoom = positiveOr(read(state2.model, "Zoom", 1), 1);
                        state2.pinchLastZoom = state2.pinchStartZoom;
                    },
                    onChange(info) {
                        commitPinchZoom(root, info?.scale);
                    },
                    onEnd(info) {
                        commitPinchZoom(root, info?.scale);
                    },
                    onCancel() {
                        // Escape / pointercancel / lost capture: the pinch rolls back and commits nothing.
                        const state2 = getState(root);
                        if (state2) state2.pinchLastZoom = state2.pinchStartZoom;
                    }
                });

                session.drag = touchDrag;
                session.pinch = pinch;
                session.ready = true;
            })
            .catch(err => {
                console.warn("tmSpreadsheetCanvas: the shared pointer helpers (pointer-drag.js, pointer-gesture.js) "
                    + "could not be loaded; the grid keeps its mouse interaction", err);
            });
    }

    /**
     * Cancels the canvas-owned single-finger gestures a pinch takes over: the fill drag, the selection
     * drag, a pending drag candidate, the autoscroll loop and a queued handle tap. Without this a finger
     * that had started a fill kept extending it while the other two fingers zoomed.
     */
    function cancelCanvasGesturesForPinch(root) {
        const s = getState(root);
        if (!s) return;
        let repaint = false;
        if (getAutoFillState(root)?.active) {
            clearAutoFillDrag(root);
            repaint = true;
        }
        if (getSelectionDrag(root)) {
            setSelectionDrag(root, null);
            repaint = true;
        }
        setPossibleDrag(root, null);
        stopDragAutoscroll(root);
        if (repaint) requestCanvasRedraw(root, "pinch", "full");
    }

    /** Turns one pinch scale into the absolute, clamped, step-quantized zoom .NET owns. */
    function commitPinchZoom(root, scale) {
        const s = getState(root);
        if (!s) return false;
        const decision = resolvePinchZoom({ startZoom: s.pinchStartZoom || 1, scale: positiveOr(scale, 1) });
        if (!decision.changed || Math.abs(decision.zoom - (s.pinchLastZoom || 0)) < 0.001) return false;
        s.pinchLastZoom = decision.zoom;
        invokeDotNet(root, "OnCanvasZoomChanged", [decision.zoom], true).catch(() => {});
        return true;
    }

    /**
     * The density a pointer-kind CHANGE re-resolves to, or `reResolve: false` when the host pinned the
     * density (an explicit Compact/Comfortable is not the browser's business). Pure so the node lane
     * can pin the "only Auto follows the pointer" rule.
     */
    function resolvePointerKindDensityDecision(input) {
        const opts = input || {};
        const requested = String(opts.requested ?? "").trim().toLowerCase();
        const coarse = !!opts.coarse;
        const resolved = resolveDensity(requested, coarse);
        return {
            reResolve: requested !== "compact" && requested !== "comfortable",
            density: resolved.density,
            coarse,
            gutterScale: resolved.gutterScale
        };
    }

    /**
     * Attaches exactly one `(pointer: coarse)` change listener. `requestedDensity` is a value or a
     * getter (the model can be re-rendered with another density while the grid lives). Returns the
     * listener so `dispose` can detach it, or null when the query has no addEventListener (a host
     * without matchMedia keeps the density it resolved on the first frame).
     */
    function attachPointerKindListener(query, requestedDensity, notify) {
        if (!query || typeof query.addEventListener !== "function") return null;
        const onChange = event => {
            const requested = typeof requestedDensity === "function" ? requestedDensity() : requestedDensity;
            const decision = resolvePointerKindDensityDecision({ requested, coarse: !!event?.matches });
            if (!decision.reResolve) return;
            if (typeof notify === "function") notify(decision.density, decision.coarse);
        };
        try { query.addEventListener("change", onChange); } catch (e) { return null; }
        return onChange;
    }

    /**
     * Watches the `(pointer: coarse)` media query for the LIFETIME of a grid, so attaching a mouse to a
     * tablet (or undocking a 2-in-1) re-resolves the Auto density instead of keeping the first answer.
     */
    function observePointerKind(root, dotNet) {
        const s = getState(root);
        if (!s || !window.matchMedia) return;
        let query = null;
        try { query = window.matchMedia("(pointer: coarse)"); } catch (e) { return; }
        const onChange = attachPointerKindListener(
            query,
            () => read(getState(root)?.model, "Density", "auto"),
            (_density, coarse) => {
                // .NET re-resolves the density itself (an explicit Compact/Comfortable stays pinned), so
                // only the coarse flag crosses the boundary.
                invokeDotNet(root, "OnCanvasPointerKindChanged", [coarse], true).catch(() => {});
            });
        if (!onChange) return;
        s.pointerKindQuery = query;
        s.pointerKindListener = onChange;
    }

    /**
     * The activation a PRIMARY press produces on the grid surface: select the cell, pick a formula
     * reference, or forward the empty-area pointer. Phase 7 extracted it from `click` so the touch
     * tap can reuse exactly ONE implementation instead of a near copy (the touch path calls this
     * and swallows the compatibility click that follows).
     */
    function activateCanvasPointer(rootElement, ev) {
        const s = getState(rootElement);
        const p = toContentPoint(rootElement, ev);
        const hit = hitCell(rootElement, p);
        if (hit) {
            const cell = findCell(getState(rootElement)?.model, hit.row, hit.col);
            const hyperlink = read(cell, "Hyperlink", null);
            if (hyperlink && (ev.ctrlKey || ev.metaKey)) {
                ev.preventDefault?.();
                handleHyperlinkClick(rootElement, hyperlink);
                return;
            }

            if (isFormulaPointMode(rootElement) && s.editor) {
                if (isFormulaEditorSelfHit(rootElement, hit)) {
                    ignoreFormulaEditorSelfHit(rootElement);
                } else {
                    beginFormulaReferenceDrag(rootElement, hit);
                    endFormulaReferenceDrag(rootElement);
                    s.editor.input.focus({ preventScroll: true });
                }
                ev.preventDefault?.();
                return;
            }

            if (isFormulaPointMode(rootElement)) {
                if (isExternalFormulaSelfHit(rootElement, hit)) {
                    if (Number(ev.detail || 0) >= 2) {
                        updateLocalActiveCell(rootElement, hit.row, hit.col, false, "pointer");
                        openLocalEditor(rootElement, hit);
                    }
                    ev.preventDefault?.();
                    return;
                }
                invokeDotNet(rootElement, "OnCanvasCellPointer", [hit.row, hit.col, !!ev.shiftKey, !!ev.ctrlKey], true).catch(() => {});
                ev.preventDefault?.();
                return;
            }

            closeLocalEditor(rootElement, true);
            if (!isFormulaPointMode(rootElement)) {
                updateLocalActiveCell(rootElement, hit.row, hit.col, !!ev.shiftKey, "pointer");
                flushSelectionSettled(rootElement);
            }
            invokeDotNet(rootElement, "OnCanvasCellPointer", [hit.row, hit.col, !!ev.shiftKey, !!ev.ctrlKey], true).catch(() => {});
            return;
        }

        if (isFormulaPointMode(rootElement) && s.editor) {
            preserveFormulaEditorFocus(rootElement);
            ev.preventDefault?.();
            return;
        }

        invokeDotNet(rootElement, "OnCanvasPointer", [p.x, p.y, !!ev.shiftKey, !!ev.ctrlKey], true).catch(() => {});
    }

    window.tmSpreadsheetCanvas.register = function (root, canvas, headerCanvas, selectionCanvas, dotNet) {
        if (!dotNet) {
            dotNet = selectionCanvas;
            selectionCanvas = null;
            if (headerCanvas && !headerCanvas.getContext) {
                selectionCanvas = null;
            } else if (headerCanvas && headerCanvas.getContext) {
                selectionCanvas = headerCanvas;
            }
            headerCanvas = null;
        }
        if (!root || !canvas || !dotNet) return;
        window.tmSpreadsheetCanvas.dispose(root);

        const renderer = createRendererState(canvas, headerCanvas, selectionCanvas, root);
        const workbookState = createWorkbookState(root);
        const sheetState = workbookState.sheets[workbookState.activeSheetId];
        const s = {
            canvas,
            headerCanvas,
            selectionCanvas,
            dotNet,
            renderer,
            model: null,
            workbookState,
            sheetState,
            blazorRevisionCounter: 0,
            interactionVersion: 0,
            lastInteractionSource: "",
            paintFrame: 0,
            paintRequested: false,
            pendingPaintRequestCount: 0,
            dirtyKind: "",
            dirtyReason: "",
            syncFrame: 0,
            syncRequested: false,
            syncForce: false,
            viewportTimer: 0,
            viewportInFlight: false,
            viewportPending: false,
            viewportPendingForce: false,
            logicalScrollLeft: root.scrollLeft || 0,
            logicalScrollTop: root.scrollTop || 0,
            paintedScrollLeft: root.scrollLeft || 0,
            paintedScrollTop: root.scrollTop || 0,
            nativeScrollSyncTimer: 0,
            ownScrollUntil: 0,
            lastLogicalKeyboardScrollAt: 0,
            lastNativeScrollInput: "",
            keyboardRepeatKey: "",
            keyboardRepeatStartedAt: 0,
            keyboardRepeatLastAt: 0,
            keyboardRepeatCount: 0,
            pointerFrame: 0,
            pointerPoint: null,
            hoverCell: null,
            possibleDrag: null,
            selectionDrag: null,
            dragAutoscrollFrame: 0,
            dragAutoscrollPointer: null,
            pasteBuffer: [],
            pasteTimer: 0,
            suppressClick: false,
            blockNextClick: false,
            forceFormulaGestureUntil: 0,
            recentFormulaSessionUntil: 0,
            lastPointerButton: 0,
            nonPrimaryGestureUntil: 0,
            selectionSyncFrame: 0,
            selectionInFlight: false,
            selectionPending: false,
            cellEditCommitTimer: 0,
            pendingCellEditCommits: [],
            commandLog: [],
            commandLogSeq: 0,
            commandLogAckRevision: 0,
            commandLogTimer: 0,
            commandLogInFlight: false,
            commandLogPending: false,
            lastViewportSync: 0,
            syncedScrollLeft: root.scrollLeft || 0,
            syncedScrollTop: root.scrollTop || 0,
            editor: null,
            palette: buildPalette(root),
            textMetricsCache: renderer.cache.textMetrics,
            fontStringCache: renderer.cache.fonts,
            paintStyleCache: renderer.cache.paintStyles,
            displayValueCache: renderer.cache.displayValues,
            cellSnapshotCache: renderer.cache.cellSnapshots,
            fontSignature: "",
            visibleLayoutCache: null,
            metrics: createDebugMetrics(),
            resize: null,
            // Phase 7 touch state: the two shared-helper sessions, the DOM handle layer cache, the
            // pending double-tap candidate and the pinch's start zoom (the model's Zoom lags a frame).
            touch: null,
            touchTapCandidate: null,
            touchHandles: null,
            pinchStartZoom: 1,
            pinchLastZoom: 1,
            touchClickGuard: null,
            touchMenuHold: false,
            pointerKindQuery: null,
            pointerKindListener: null,
            listeners: []
        };

        const onScroll = () => {
            const now = performance.now();
            const ownScroll = now < (s.ownScrollUntil || 0);
            if (ownScroll) {
                if (s.metrics) {
                    s.metrics.nativeScrollEventCount += 1;
                    s.metrics.ignoredOwnNativeScrollEventCount += 1;
                    s.metrics.ownNativeScrollEventCount += 1;
                    s.metrics.nativeScrollLeft = root.scrollLeft || 0;
                    s.metrics.nativeScrollTop = root.scrollTop || 0;
                }
                updateLocalEditorPosition(root);
                return;
            }

            const nativeBehindLogical = s.lastInteractionSource === "keyboard"
                && now - (s.lastLogicalKeyboardScrollAt || 0) < 180
                && (Math.abs((root.scrollLeft || 0) - getLogicalScrollLeft(root)) > 0.5
                    || Math.abs((root.scrollTop || 0) - getLogicalScrollTop(root)) > 0.5);
            if (nativeBehindLogical) {
                if (s.metrics) {
                    s.metrics.nativeScrollEventCount += 1;
                    s.metrics.staleNativeScrollEventCount += 1;
                    s.metrics.nativeScrollLeft = root.scrollLeft || 0;
                    s.metrics.nativeScrollTop = root.scrollTop || 0;
                }
                scheduleNativeScrollSync(root, 30);
                updateLocalEditorPosition(root);
                return;
            }

            syncLogicalScrollFromNative(root, "native");
            updateLocalEditorPosition(root);
            if (s.metrics) {
                s.metrics.nativeScrollEventCount += 1;
                s.metrics.userNativeScrollEventCount += 1;
                if (s.lastNativeScrollInput === "wheel") s.metrics.wheelNativeScrollEventCount += 1;
                else s.metrics.scrollbarNativeScrollEventCount += 1;
                s.metrics.scrollInteractions += 1;
            }
            s.lastNativeScrollInput = "";
            scheduleLocalRender(root);
            notifyViewport(root, false);
        };
        const onWheel = ev => {
            if (!s.model) return;
            // A wheel over an open dialog scrolls the dialog's own body, never the grid behind it.
            if (isSpreadsheetOverlayTarget(ev.target)) return;

            // Ctrl + wheel zooms the grid; the zoom factor itself lives in .NET state.
            if (ev.ctrlKey || ev.metaKey) {
                ev.preventDefault();
                const direction = ev.deltaY < 0 ? 1 : -1;
                invokeDotNet(root, "OnCanvasZoomDelta", [direction], true);
                return;
            }

            const delta = normalizeWheelDelta(ev, root);
            if (!delta.dx && !delta.dy) return;

            s.lastNativeScrollInput = "wheel";
            if (s.metrics) {
                s.metrics.wheelEventCount += 1;
                s.metrics.wheelPreventedCount += 1;
            }

            ev.preventDefault();
            markLocalInteraction(root, "scroll");
            const changed = setLogicalScroll(
                root,
                getLogicalScrollLeft(root) + delta.dx,
                getLogicalScrollTop(root) + delta.dy,
                "wheel");

            if (!changed) return;

            requestPaint(root, "scroll", "full");
            scheduleNativeScrollSync(root, 120);
            debounceViewportSyncAfterPaint(root, false, 120);
        };
        const clearTouchHover = () => {
            if (!getSheetHover(root)) return;
            setSheetHover(root, null);
            requestCanvasRedraw(root, "pointer", "selection");
        };
        const updatePointerCursor = () => {
            s.pointerFrame = 0;
            const point = s.pointerPoint;
            if (!point) return;
            // A hovering FINGER is meaningless (it is gone the moment it lands elsewhere) and the tint it
            // leaves behind follows the last touch point around the grid.
            if (s.pointerKind === "touch") return;
            if (s.resize) {
                root.style.cursor = s.resize.kind === "column" ? "col-resize" : "row-resize";
                return;
            }

            const hit = hitResize(root, point);
            root.style.cursor = hit ? (hit.kind === "column" ? "col-resize" : "row-resize") : "";
            if (!hit && s.model && !getSelectionDrag(root)) {
                const cellHit = hitCell(root, toContentPoint(root, point));
                const nextHover = cellHit ? { row: cellHit.row, col: cellHit.col } : null;
                if (!sameCell(nextHover, getSheetHover(root))) {
                    setSheetHover(root, nextHover);
                    requestCanvasRedraw(root, "pointer", "selection");
                }
            }
        };
        const onPointerMove = ev => {
            s.pointerPoint = { clientX: ev.clientX, clientY: ev.clientY };
            s.pointerKind = resolvePointerKind(ev.pointerType);
            if (s.resize) {
                updateResizeSession(root, ev);
                ev.preventDefault();
                return;
            }
            if (s.sheetState?.formulaEditor?.active && s.sheetState.formulaEditor.dragAnchor) {
                const hit = hitCell(root, toContentPoint(root, ev));
                if (hit) updateFormulaReferenceDrag(root, hit);
                ev.preventDefault();
                return;
            }

            if (getAutoFillState(root)?.active) {
                updateAutoFillPreview(root, ev.clientX, ev.clientY);
                updateDragAutoscroll(root, ev.clientX, ev.clientY, ev.pointerId);
                ev.preventDefault();
                return;
            }

            if (getSelectionDrag(root)) {
                updateDragSelectionTarget(root, ev.clientX, ev.clientY);
                updateDragAutoscroll(root, ev.clientX, ev.clientY, ev.pointerId);
                ev.preventDefault();
                return;
            }

            if (isFormulaPointMode(root) && !s.editor && s.sheetState?.externalFormulaPicker?.active) {
                const hit = hitCell(root, toContentPoint(root, ev)) || getLocalCellFromClientPoint(root, ev.clientX, ev.clientY);
                updateExternalFormulaReferenceDrag(root, hit, ev);
                ev.preventDefault();
                return;
            }

            const possibleDrag = getPossibleDrag(root);
            if (possibleDrag) {
                const dx = ev.clientX - possibleDrag.clientX;
                const dy = ev.clientY - possibleDrag.clientY;
                if (Math.abs(dx) + Math.abs(dy) > 4) {
                    setSelectionDrag(root, { row: possibleDrag.row, col: possibleDrag.col });
                    s.suppressClick = true;
                    safeSetPointerCapture(root, ev.pointerId);
                    // A selection-handle drag keeps the existing anchor and only moves the END; a body
                    // drag (mouse/pen only) re-anchors on the pressed cell first.
                    if (!possibleDrag.fromSelectionHandle) {
                        updateLocalActiveCell(root, possibleDrag.row, possibleDrag.col, false, "pointer");
                    }
                    updateDragSelectionTarget(root, ev.clientX, ev.clientY);
                    updateDragAutoscroll(root, ev.clientX, ev.clientY, ev.pointerId);
                    ev.preventDefault();
                    return;
                }
            }

            if (!s.pointerFrame) {
                s.pointerFrame = requestAnimationFrame(updatePointerCursor);
            }
        };
        const onPointerDown = ev => {
            const hit = hitResize(root, ev);
            if (!hit) return;
            beginResizeSession(root, hit, ev);
            safeSetPointerCapture(root, ev.pointerId);
            ev.preventDefault();
        };
        const onMouseDown = ev => {
            s.lastPointerButton = Number(ev.button || 0);
            if (ev.button !== 0) {
                s.blockNextClick = true;
                if (shouldGuardFormulaGesture(root)) {
                    s.forceFormulaGestureUntil = performance.now() + 1000;
                }
            }
            if (ev.button !== 0 && isFormulaPointMode(root)) {
                s.nonPrimaryGestureUntil = performance.now() + nonPrimaryGestureBlockMs;
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                preserveFormulaBarFocus(root);
            }
            if (ev.button === 0) return;
            if (!isFormulaPointMode(root)) return;
            s.suppressClick = true;
            if (s.editor) preserveFormulaEditorFocus(root);
            setPossibleDrag(root, null);
            ev.preventDefault();
            ev.stopPropagation();
        };
        const onAuxClick = ev => {
            s.lastPointerButton = Number(ev.button || 0);
            if (ev.button !== 0) {
                s.blockNextClick = true;
                if (shouldGuardFormulaGesture(root)) {
                    s.forceFormulaGestureUntil = performance.now() + 1000;
                }
            }
            if (ev.button !== 0 && isFormulaPointMode(root)) {
                s.nonPrimaryGestureUntil = performance.now() + nonPrimaryGestureBlockMs;
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                preserveFormulaBarFocus(root);
            }
            if (ev.button === 0) return;
            if (isFormulaPointMode(root) && s.editor) {
                s.suppressClick = true;
                preserveFormulaEditorFocus(root);
                ev.preventDefault();
                ev.stopPropagation();
            }
        };
        const onPointerDownWrapper = ev => {
            s.lastPointerButton = Number(ev.button || 0);
            // A second finger belongs to the pinch, never to the canvas' single-pointer path.
            if (resolvePointerKind(ev.pointerType) === "touch") {
                if (ev.isPrimary === false) return;
                clearTouchHover();
            }
            if (ev.button !== 0) {
                s.blockNextClick = true;
                if (shouldGuardFormulaGesture(root)) {
                    s.forceFormulaGestureUntil = performance.now() + 1000;
                }
            }
            if (ev.button !== 0 && isFormulaPointMode(root)) {
                s.nonPrimaryGestureUntil = performance.now() + nonPrimaryGestureBlockMs;
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                preserveFormulaBarFocus(root);
            }
            if (isSpreadsheetOverlayTarget(ev.target)) {
                setPossibleDrag(root, null);
                return;
            }

            if (ev.button !== 0 && s.model && isFormulaPointMode(root)) {
                preserveFormulaBarFocus(root);
                if (s.editor) preserveFormulaEditorFocus(root);
                setPossibleDrag(root, null);
                s.suppressClick = true;
                ev.preventDefault();
                ev.stopPropagation();
                return;
            }

            if (ev.button === 0 && s.model && isFormulaPointMode(root) && s.editor) {
                const resizeHit = hitResize(root, ev);
                const hit = hitCell(root, toContentPoint(root, ev));
                if (resizeHit || !hit) {
                    preserveFormulaEditorFocus(root);
                    setPossibleDrag(root, null);
                    s.suppressClick = true;
                    ev.preventDefault();
                    ev.stopPropagation();
                    return;
                }

                if (isFormulaEditorSelfHit(root, hit)) {
                    ignoreFormulaEditorSelfHit(root);
                    setPossibleDrag(root, null);
                    s.suppressClick = true;
                    ev.preventDefault();
                    ev.stopPropagation();
                    return;
                }

                beginFormulaReferenceDrag(root, hit);
                safeSetPointerCapture(root, ev.pointerId);
                ev.preventDefault();
                ev.stopPropagation();
                return;
            }

            if (ev.button === 0 && s.model && isFormulaPointMode(root) && !s.editor) {
                const resizeHit = hitResize(root, ev);
                const hit = hitCell(root, toContentPoint(root, ev));
                if (resizeHit) {
                    clearExternalFormulaPicker(root);
                    onPointerDown(ev);
                    return;
                }

                if (!hit) {
                    clearExternalFormulaPicker(root);
                    ev.preventDefault();
                    ev.stopPropagation();
                    return;
                }

                if (isExternalFormulaSelfHit(root, hit)) {
                    clearExternalFormulaPicker(root);
                    setPossibleDrag(root, null);
                    return;
                }

                beginExternalFormulaReferenceDrag(root, hit, ev);
                safeSetPointerCapture(root, ev.pointerId);
                ev.preventDefault();
                ev.stopPropagation();
                return;
            }

            if (ev.button !== 0) {
                onPointerDown(ev);
                return;
            }

            const resizeHit = hitResize(root, ev);
            if (resizeHit) {
                onPointerDown(ev);
                return;
            }

            if (s.model && !isFormulaPointMode(root) && !isFormatPainterActive(root)) {
                const filterBtn = hitFilterButton(root, ev);
                if (filterBtn) {
                    // Do not bump the local revision here: the filter dropdown applies through a
                    // C# command whose frame would otherwise be treated as stale and dropped.
                    invokeDotNet(root, "CanvasFilterButtonClicked", [filterBtn.col, ev.clientX, ev.clientY], false);
                    s.suppressClick = true;
                    setPossibleDrag(root, null);
                    ev.preventDefault();
                    ev.stopPropagation();
                    return;
                }

                const validationDropBtn = hitValidationDropButton(root, ev);
                if (validationDropBtn) {
                    invokeDotNet(root, "CanvasValidationDropButtonClicked", [validationDropBtn.row, validationDropBtn.col, ev.clientX, ev.clientY], false);
                    s.suppressClick = true;
                    setPossibleDrag(root, null);
                    ev.preventDefault();
                    ev.stopPropagation();
                    return;
                }
            }

            // Item 7: a finger that landed on the DOM fill box owns the fill drag even where the
            // coordinate hit test (density/coarse sized) does not reach.
            const touchFillHandle = readTouchHandleTarget(ev) === "fill"
                && resolvePointerKind(ev.pointerType) === "touch";
            if (s.model && !isFormulaPointMode(root) && !isFormatPainterActive(root)
                && (hitAutoFillHandle(root, ev) || touchFillHandle)) {
                beginAutoFillDrag(root, ev.pointerId);
                safeSetPointerCapture(root, ev.pointerId);
                ev.preventDefault();
                ev.stopPropagation();
                return;
            }

            // Phase 7: a finger that grabbed a selection-handle marker starts a selection drag from the
            // CURRENT anchor. The marker owns touch-action: none, so the browser cannot pan under it.
            const touchHandle = readTouchHandleTarget(ev);
            if (touchHandle && touchHandle !== "fill" && resolvePointerKind(ev.pointerType) === "touch") {
                // A round corner drop extends the selection BY DRAGGING (Shift+arrow is the keyboard
                // alternative, the Fill entries cover the fill), so a tap on it does nothing at all —
                // it must never move the selection to the neighbouring cell under the drop.
                const bounds = selectionBounds(getSelectionSnapshot(root));
                setSelectionDrag(root, null);
                setPossibleDrag(root, {
                    row: bounds.endRow,
                    col: bounds.endCol,
                    clientX: ev.clientX,
                    clientY: ev.clientY,
                    fromSelectionHandle: touchHandle
                });
                s.suppressClick = true;
                safeSetPointerCapture(root, ev.pointerId);
                return;
            }

            if (s.model && !isFormulaPointMode(root) && !isFormatPainterActive(root)) {
                const hit = hitCell(root, toContentPoint(root, ev));
                // Phase 7: a FINGER on a cell pans the grid (the browser scrolls) — the canvas only
                // starts its own drag-select for a mouse/pen, or from a selection-handle marker above.
                const canDragSelect = resolvePointerKind(ev.pointerType) !== "touch";
                setPossibleDrag(root, hit && canDragSelect
                    ? { row: hit.row, col: hit.col, clientX: ev.clientX, clientY: ev.clientY }
                    : null);
            }
        };
        const onPointerUp = ev => {
            if (ev.button !== 0 && isFormulaPointMode(root)) {
                if (s.sheetState?.formulaEditor?.active && s.editor?.input) {
                    s.editor.input.focus({ preventScroll: true });
                }
                preserveFormulaBarFocus(root);
                restoreExternalFormulaOrigin(root);
                setPossibleDrag(root, null);
                safeReleasePointerCapture(root, ev.pointerId);
                s.suppressClick = true;
                ev.preventDefault();
                ev.stopPropagation();
                return;
            }

            if (s.sheetState?.formulaEditor?.active && s.sheetState.formulaEditor.dragAnchor) {
                const hit = hitCell(root, toContentPoint(root, ev));
                if (hit) updateFormulaReferenceDrag(root, hit);
                endFormulaReferenceDrag(root);
                safeReleasePointerCapture(root, ev.pointerId);
                s.editor?.input?.focus({ preventScroll: true });
                ev.preventDefault();
                return;
            }

            if (isFormulaPointMode(root) && !s.editor) {
                const hit = hitCell(root, toContentPoint(root, ev)) || getLocalCellFromClientPoint(root, ev.clientX, ev.clientY);
                if (hit && isExternalFormulaSelfHit(root, hit) && Number(ev.detail || 0) >= 2) {
                    updateLocalActiveCell(root, hit.row, hit.col, false, "pointer");
                    openLocalEditor(root, hit);
                    ev.preventDefault();
                    return;
                }
            }

            if (isFormulaPointMode(root) && !s.editor && s.sheetState?.externalFormulaPicker?.active) {
                const hit = hitCell(root, toContentPoint(root, ev)) || getLocalCellFromClientPoint(root, ev.clientX, ev.clientY);
                endExternalFormulaReferenceDrag(root, hit);
                safeReleasePointerCapture(root, ev.pointerId);
                ev.preventDefault();
                return;
            }

            if (getAutoFillState(root)?.active) {
                updateAutoFillPreview(root, ev.clientX, ev.clientY);
                commitAutoFill(root);
                stopDragAutoscroll(root);
                safeReleasePointerCapture(root, ev.pointerId);
                ev.preventDefault();
                return;
            }

            if (getSelectionDrag(root)) {
                setSelectionDrag(root, null);
                setPossibleDrag(root, null);
                stopDragAutoscroll(root);
                safeReleasePointerCapture(root, ev.pointerId);
                if (resolvePointerKind(ev.pointerType) === "touch") clearTouchHover();
                scheduleSelectionSync(root);
                ev.preventDefault();
                return;
            }

            setPossibleDrag(root, null);
            if (!s.resize) return;
            const resize = s.resize;
            safeReleasePointerCapture(root, ev.pointerId);
            updateResizeSession(root, ev);
            clearResizePreview(root);
            const next = Math.max(resize.kind === "column" ? 16 : 8, resize.currentSize || resize.size);
            if (Math.abs(next - resize.size) > 0.25) {
                markLocalInteraction(root, "resize");
                setLayoutAxisSize(root, resize.kind, resize.index, next);
                addResizeCommitDirtyRects(root, resize.kind, resize.index);
                queueCommand(root, resize.kind === "column" ? "columnResized" : "rowResized", {
                    axis: resize.kind,
                    index: resize.index,
                    size: next
                }, {
                    flushNow: true
                });
                requestCanvasRedraw(root, "resize-commit", "content");
            }
            ev.preventDefault();
        };
        const onKeyDown = ev => {
            if (isEditableKeyTarget(ev.target) || isSpreadsheetOverlayTarget(ev.target)) return;
            if (s.editor) return;
            if (handleNavigationKey(root, ev)) return;
            handleCommandKey(root, ev);
        };
        const onClick = ev => {
            if (s.blockNextClick) {
                s.blockNextClick = false;
                ev.preventDefault();
                return;
            }
            if (performance.now() < Number(s.nonPrimaryGestureUntil || 0)) {
                ev.preventDefault();
                return;
            }
            if (Number(s.lastPointerButton || 0) !== 0) {
                s.lastPointerButton = 0;
                ev.preventDefault();
                return;
            }
            if (isSpreadsheetOverlayTarget(ev.target)) {
                ev.preventDefault();
                return;
            }
            if (s.suppressClick) {
                s.suppressClick = false;
                ev.preventDefault();
                return;
            }
            if (s.resize) return;
            activateCanvasPointer(root, ev);
        };
        const onDblClick = ev => {
            if (root.dataset.readonly === "true") {
                ev.preventDefault();
                return;
            }
            if (performance.now() < Number(s.nonPrimaryGestureUntil || 0)) {
                ev.preventDefault();
                return;
            }
            if (Number(s.lastPointerButton || 0) !== 0) {
                s.lastPointerButton = 0;
                ev.preventDefault();
                return;
            }
            if (isSpreadsheetOverlayTarget(ev.target)) {
                ev.preventDefault();
                return;
            }
            const p = toContentPoint(root, ev);
            const hit = hitCell(root, p);
            if (hit) {
                if (isFormulaPointMode(root) && !s.editor && isExternalFormulaSelfHit(root, hit)) {
                    updateLocalActiveCell(root, hit.row, hit.col, false, "pointer");
                    openLocalEditor(root, hit);
                    ev.preventDefault();
                    return;
                }
                updateLocalActiveCell(root, hit.row, hit.col, false, "pointer");
                flushSelectionSettled(root);
                openLocalEditor(root, hit);
                return;
            }

            invokeDotNet(root, "OnCanvasDoubleClick", [p.x, p.y], true).catch(() => {});
        };
        const onContextMenu = ev => {
            ev.preventDefault();
            if (performance.now() < Number(s.recentFormulaSessionUntil || 0) || hasRecentFormulaBarFormulaSession(root)) {
                preserveFormulaBarFocus(root);
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                s.suppressClick = true;
                ev.stopPropagation();
                return;
            }
            if (performance.now() < Number(s.forceFormulaGestureUntil || 0)) {
                preserveFormulaBarFocus(root);
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                s.suppressClick = true;
                ev.stopPropagation();
                return;
            }
            if (performance.now() < Number(s.nonPrimaryGestureUntil || 0)) {
                ev.stopPropagation();
                return;
            }
            if (s.suppressClick && Number(s.lastPointerButton || 0) !== 0) {
                preserveFormulaBarFocus(root);
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                s.nonPrimaryGestureUntil = performance.now() + nonPrimaryGestureBlockMs;
                ev.stopPropagation();
                return;
            }
            if (isSpreadsheetOverlayTarget(ev.target)) {
                ev.stopPropagation();
                return;
            }

            if (isFormulaPointMode(root)) {
                if (s.sheetState?.formulaEditor?.active && s.editor?.input) {
                    s.editor.input.focus({ preventScroll: true });
                }
                preserveFormulaBarFocus(root);
                restoreExternalFormulaOrigin(root);
                scheduleExternalFormulaOriginRestore(root);
                s.suppressClick = true;
                ev.stopPropagation();
                return;
            }
            s.suppressClick = false;
            s.lastPointerButton = 0;
            const p = toContentPoint(root, ev);
            // The SAME six arguments as the touch path: a JSInvokable call must match the C# signature
            // exactly (a short args array throws and kills every later interop on the page).
            const viewportHeight = Math.max(0, Number(window.innerHeight) || 0);
            invokeDotNet(root, "OnCanvasContextMenu",
                [p.x, p.y, ev.clientX, ev.clientY, "pointer", viewportHeight], true).catch(() => {});
        };
        const onCopy = ev => {
            if (!isJsEngine(root)) return;
            const snapshot = getSelectionSnapshot(root);
            const text = selectionToClipboardText(root, snapshot);
            const payload = buildClipboardPayload(root, snapshot, false);
            ev.preventDefault();
            ev.clipboardData?.setData("text/plain", text);
            try { ev.clipboardData?.setData(customClipboardMime, JSON.stringify(payload)); } catch { }
            invokeDotNetAfterPendingState(root, "OnCanvasKeyCommand", ["c", false, true, false, false], true).catch(() => {});
        };
        const onCut = ev => {
            if (!isJsEngine(root)) return;
            const snapshot = getSelectionSnapshot(root);
            const text = selectionToClipboardText(root, snapshot);
            const payload = buildClipboardPayload(root, snapshot, true);
            ev.preventDefault();
            ev.clipboardData?.setData("text/plain", text);
            try { ev.clipboardData?.setData(customClipboardMime, JSON.stringify(payload)); } catch { }
            invokeDotNetAfterPendingState(root, "OnCanvasKeyCommand", ["x", false, true, false, false], true).catch(() => {});
        };
        const onPaste = ev => {
            if (!isJsEngine(root)) return;
            const custom = ev.clipboardData?.getData(customClipboardMime);
            const text = ev.clipboardData?.getData("text/plain") || "";
            ev.preventDefault();
            if (custom) {
                try {
                    if (applyClipboardPayload(root, JSON.parse(custom)) > 0) return;
                } catch { }
            }
            if (applyClipboardText(root, text) > 0) return;
            invokeDotNetAfterPendingState(root, "OnCanvasKeyCommand", ["v", false, true, false, false], true).catch(() => {});
        };
        const onFocusOut = ev => {
            const nextTarget = ev?.relatedTarget;
            if (nextTarget instanceof Node && root.contains(nextTarget)) return;
            setTimeout(() => {
                const active = document.activeElement;
                if (active === root || (active instanceof Node && root.contains(active))) return;
                syncNativeScrollFromLogical(root);
                flushSelectionSettled(root);
                requestViewportSync(root, true);
            }, 0);
        };

        root.addEventListener("scroll", onScroll, { passive: true });
        root.addEventListener("wheel", onWheel, { passive: false });
        root.addEventListener("pointermove", onPointerMove);
        root.addEventListener("mousedown", onMouseDown, true);
        root.addEventListener("auxclick", onAuxClick, true);
        root.addEventListener("pointerdown", onPointerDownWrapper);
        root.addEventListener("pointerup", onPointerUp);
        root.addEventListener("keydown", onKeyDown, true);
        root.addEventListener("click", onClick);
        root.addEventListener("dblclick", onDblClick);
        root.addEventListener("contextmenu", onContextMenu);
        root.addEventListener("copy", onCopy);
        root.addEventListener("cut", onCut);
        root.addEventListener("paste", onPaste);
        root.addEventListener("focusout", onFocusOut);
        s.listeners.push(
            ["scroll", onScroll],
            ["wheel", onWheel, { passive: false }],
            ["pointermove", onPointerMove],
            ["mousedown", onMouseDown, true],
            ["auxclick", onAuxClick, true],
            ["pointerdown", onPointerDownWrapper],
            ["pointerup", onPointerUp],
            ["keydown", onKeyDown, true],
            ["click", onClick],
            ["dblclick", onDblClick],
            ["contextmenu", onContextMenu],
            ["copy", onCopy],
            ["cut", onCut],
            ["paste", onPaste],
            ["focusout", onFocusOut]
        );

        if (typeof ResizeObserver !== "undefined") {
            s.resizeObserver = new ResizeObserver(() => {
                s.palette = buildPalette(root);
                clearCellSnapshots(root, "resize");
                invalidateVisibleLayout(root, "resize-observer");
                notifyViewport(root, true);
                if (s.model) requestCanvasRedraw(root, "resize", "full");
            });
            s.resizeObserver.observe(root);
        }

        // The state must be reachable through getState() BEFORE the phase-7 sessions attach: both read
        // it (unlike the observable helpers below, which already had it).
        root[stateKey] = s;
        syncAccessibilityState(root, "immediate");

        // Phase 7: the touch sessions (shared pointer-drag.js / pointer-gesture.js) and the pointer-kind
        // observer. Both degrade gracefully — without the helpers the grid keeps its mouse path, and
        // without matchMedia the Auto density simply keeps the first answer.
        attachTouchGestures(root);
        observePointerKind(root, dotNet);

        notifyViewport(root, true);
    };

    window.tmSpreadsheetCanvas.initEngine = function (root, canvas, headerCanvas, selectionCanvas, dotNet, model) {
        if (!root || !canvas || !dotNet) return;
        window.tmSpreadsheetCanvas.register(root, canvas, headerCanvas, selectionCanvas, dotNet);
        if (model) {
            window.tmSpreadsheetCanvas.render(root, canvas, model);
        }
    };

    window.tmSpreadsheetCanvas.dispose = function (root) {
        const s = getState(root);
        if (!root || !s) return;
        for (const [event, listener, options] of s.listeners || []) {
            root.removeEventListener(event, listener, options);
        }
        if (s.resizeObserver) s.resizeObserver.disconnect();
        try { s.touch?.detach?.(); } catch (e) { /* the helpers were never loaded */ }
        s.touch = null;
        disarmTouchReleaseClickGuard(root);
        try {
            if (s.pointerKindQuery && s.pointerKindListener) {
                s.pointerKindQuery.removeEventListener("change", s.pointerKindListener);
            }
        } catch (e) { /* a media query without removeEventListener */ }
        s.pointerKindQuery = null;
        s.pointerKindListener = null;
        if (s.paintFrame) cancelAnimationFrame(s.paintFrame);
        if (s.syncFrame) cancelAnimationFrame(s.syncFrame);
        if (s.pointerFrame) cancelAnimationFrame(s.pointerFrame);
        if (s.dragAutoscrollFrame) cancelAnimationFrame(s.dragAutoscrollFrame);
        if (s.selectionSyncFrame) cancelAnimationFrame(s.selectionSyncFrame);
        if (s.postPaintDebouncedViewportFrame) cancelAnimationFrame(s.postPaintDebouncedViewportFrame);
        if (s.viewportTimer) clearTimeout(s.viewportTimer);
        if (s.nativeScrollSyncTimer) clearTimeout(s.nativeScrollSyncTimer);
        if (s.selectionSyncTimer) clearTimeout(s.selectionSyncTimer);
        const accessibility = getAccessibilityState(root);
        if (accessibility?.liveRegionTimer) clearTimeout(accessibility.liveRegionTimer);
        if (s.commandLogTimer) clearTimeout(s.commandLogTimer);
        if (s.pasteTimer) clearTimeout(s.pasteTimer);
        // Leaving the page: nothing may stay held behind a key-command barrier, or an edit committed after
        // the key press would be dropped with the state below.
        s.commandLogBarriers = [];
        flushCellEditCommitQueue(root);
        flushCommandLog(root);
        closeLocalEditor(root, false);
        root.style.cursor = "";
        delete root[stateKey];
    };

    window.tmSpreadsheetCanvas.invalidateCellSnapshots = function (root, payload) {
        const s = getState(root);
        if (!s) return;

        if (read(payload, "Clear", false)) {
            clearCellSnapshots(root, "external");
        }

        invalidateCellSnapshotRefs(root, read(payload, "Cells", []));
        invalidateCellSnapshotRows(root, read(payload, "Rows", []));
        invalidateCellSnapshotColumns(root, read(payload, "Columns", []));
        updateCacheMetrics(root);
    };

    window.tmSpreadsheetCanvas.setCells = function (root, payload) {
        const cells = Array.isArray(payload) ? payload : read(payload, "Cells", []);
        const options = Array.isArray(payload)
            ? {}
            : {
                suppressRedraw: !!read(payload, "SuppressRedraw", false),
                queueRangeCommand: !!read(payload, "QueueRangeCommand", false),
                commandDelay: Number(read(payload, "CommandDelay", 35)) || 35
            };
        return setStoreCells(root, cells || [], options);
    };

    window.tmSpreadsheetCanvas.clearCells = function (root, payload) {
        const cells = Array.isArray(payload)
            ? payload
            : read(payload, "Cells", read(payload, "Refs", []));
        const options = Array.isArray(payload)
            ? {}
            : {
                queueRangeCommand: !!read(payload, "QueueRangeCommand", false),
                commandDelay: Number(read(payload, "CommandDelay", 35)) || 35
        };
        return clearStoreCells(root, cells || [], options);
    };

    window.tmSpreadsheetCanvas.applyClipboardText = function (root, text) {
        return applyClipboardText(root, text);
    };

    window.tmSpreadsheetCanvas.applyAutoFill = function (root, row, col, source) {
        beginAutoFillDrag(root, 0);
        const autoFill = getAutoFillState(root);
        if (!autoFill?.active) return 0;
        if (source && typeof source === "object") autoFill.source = source;
        autoFill.preview = computeAutoFillPreview(autoFill.source, Number(row) || 0, Number(col) || 0);
        setAutoFillState(root, autoFill);
        return commitAutoFill(root);
    };

    window.tmSpreadsheetCanvas.applyCommand = function (root, payload) {
        if (!root || !payload) return null;
        const type = read(payload, "Type", read(payload, "Command", read(payload, "type", ""))) || "";
        switch (type) {
            case "upsertCells":
                return window.tmSpreadsheetCanvas.setCells(root, {
                    cells: read(payload, "Cells", []),
                    suppressRedraw: !!read(payload, "SuppressRedraw", false)
                });
            case "clearCells":
                return window.tmSpreadsheetCanvas.clearCells(root, {
                    cells: read(payload, "Cells", read(payload, "Refs", []))
                });
            case "invalidateSnapshots":
                return window.tmSpreadsheetCanvas.invalidateCellSnapshots(root, read(payload, "Payload", payload));
            case "syncLayoutAxes": {
                const s = getState(root);
                const model = s?.model;
                if (!s || !model) return null;
                const rows = read(payload, "Rows", null);
                const columns = read(payload, "Columns", null);
                if (Array.isArray(rows) && rows.length > 0) {
                    write(model, "Rows", mergeAxisFrames(read(model, "Rows", []), rows));
                    write(model, "rows", read(model, "Rows", []));
                }
                if (Array.isArray(columns) && columns.length > 0) {
                    write(model, "Columns", mergeAxisFrames(read(model, "Columns", []), columns));
                    write(model, "columns", read(model, "Columns", []));
                }
                write(model, "RowCount", read(payload, "RowCount", read(model, "RowCount", 0)));
                write(model, "ColumnCount", read(payload, "ColumnCount", read(model, "ColumnCount", 0)));
                write(model, "TotalWidth", read(payload, "TotalWidth", read(model, "TotalWidth", 0)));
                write(model, "TotalHeight", read(payload, "TotalHeight", read(model, "TotalHeight", 0)));
                write(model, "FreezeRowCount", read(payload, "FreezeRowCount", read(model, "FreezeRowCount", 0)));
                write(model, "FreezeColumnCount", read(payload, "FreezeColumnCount", read(model, "FreezeColumnCount", 0)));
                syncLayoutStateFromModel(root, model);
                updateLocalEditorPosition(root);
                syncEditorAccessibility(root);
                syncAccessibilityState(root, false);
                requestCanvasRedraw(root, "sync-layout-axes", "full");
                return null;
            }
            case "syncSelection": {
                const s = getState(root);
                const model = s?.model;
                if (!s || !model) return null;
                const selection = read(payload, "Selection", {});
                const row = Number(read(selection, "Row", 0)) || 0;
                const col = Number(read(selection, "Col", 0)) || 0;
                const startRow = Number(read(selection, "StartRow", row)) || row;
                const startCol = Number(read(selection, "StartCol", col)) || col;
                const endRow = Number(read(selection, "EndRow", row)) || row;
                const endCol = Number(read(selection, "EndCol", col)) || col;
                const activeRef = read(payload, "ActiveCellRef", toCellRef(row, col));
                // The header highlight follows the selection — including a programmatic move (name box,
                // Find, MoveActiveCellByAsync, formula restore), which must not leave the old strip lit.
                const beforeSelection = getSelectionSnapshot(root);
                setSheetSelection(root, row, col, startRow, startCol, endRow, endCol);
                write(model, "ActiveCellRef", activeRef);
                write(model, "activeCellRef", activeRef);
                const selectionModel = read(model, "Selection", {});
                write(selectionModel, "StartRow", startRow);
                write(selectionModel, "StartCol", startCol);
                write(selectionModel, "EndRow", endRow);
                write(selectionModel, "EndCol", endCol);
                write(model, "Selection", selectionModel);
                const scrolled = ensureCellVisibleLocal(root, row, col);
                addSelectionDirtyRectForChange(root, beforeSelection, getSelectionSnapshot(root));
                syncEditorAccessibility(root);
                syncAccessibilityState(root, false);
                requestCanvasRedraw(root, "sync-selection", scrolled ? "full" : "selection");
                return null;
            }
            case "renderModel": {
                const s = getState(root);
                const model = read(payload, "Model", null);
                if (!s?.canvas || !model) return null;
                window.tmSpreadsheetCanvas.render(root, s.canvas, model);
                return null;
            }
            case "setValidationCircles": {
                const s = getState(root);
                if (!s) return null;
                s.validationCircleRefs = read(payload, "Refs", []) ?? [];
                requestCanvasRedraw(root, "set-validation-circles", "full");
                return null;
            }
            default:
                return null;
        }
    };

    window.tmSpreadsheetCanvas.render = function (root, canvas, model) {
        if (!root || !canvas || !model) return;
        const s = getState(root);
        const receivedServerRevision = Number(read(model, "InteractionVersion", 0)) || 0;
        const localRevisionBeforeFrame = Math.max(s?.interactionVersion || 0, s?.sheetState?.localRevision || 0);
        const staleFrame = !!(s?.model && receivedServerRevision < localRevisionBeforeFrame);
        model = preserveLocalInteraction(root, model);
        syncModelViewport(root, model);
        if (s) {
            syncSheetStateFromModel(root, model, receivedServerRevision);
            syncLayoutStateFromModel(root, model);
            syncCellStoreFromModel(root, model, { allowOverwrite: !staleFrame });
            s.model = model;
            s.palette = buildPalette(root);
            s.modelRevision = (s.modelRevision || 0) + 1;
            if (s.metrics) {
                const age = performance.now() - (s.lastLocalInteractionAt || 0);
                s.metrics.blazorFrameCount += 1;
                s.metrics.lastBlazorFrameAgeMs = Number.isFinite(age) ? age : 0;
                if (age >= 0 && age < 500) s.metrics.hotPathBlazorFrameCount += 1;
                if (s.lastInteractionSource === "resize" && age >= 0 && age < 1000) {
                    s.metrics.resizeBlazorFrameCount += 1;
                }
            }
            updateLocalEditorPosition(root);
            updateActiveCellDomProxy(root);
            syncEditorAccessibility(root);
            syncAccessibilityState(root, false);
            s.syncedScrollLeft = getLogicalScrollLeft(root);
            s.syncedScrollTop = getLogicalScrollTop(root);
            const frameVersion = Number(read(model, "InteractionVersion", 0)) || 0;
            if (frameVersion > s.interactionVersion) s.interactionVersion = frameVersion;
            requestPaint(root, "dotnet-frame", "full");
            return;
        }
        renderModel(root, canvas, model);
    };

    window.tmSpreadsheetCanvas.setExternalFormulaPointMode = function (root, active) {
        const s = getState(root);
        if (!s?.sheetState) return;
        const wasActive = !!s.sheetState.externalFormulaPointActive;
        s.sheetState.externalFormulaPointActive = !!active;
        if (active) {
            if (!wasActive) {
                captureExternalFormulaOrigin(root);
            }
            s.recentFormulaSessionUntil = performance.now() + 1500;
        } else {
            s.sheetState.externalFormulaOrigin = null;
            s.sheetState.externalFormulaPointActive = false;
            clearExternalFormulaPicker(root);
            s.formulaReferenceSource = null;
            s.formulaReferenceRevision = 0;
            s.formulaReferenceCells = [];
            if (s.metrics) {
                s.metrics.formulaEditorHighlightCount = 0;
                s.metrics.formulaEditorReferenceCount = 0;
            }
            addDirtyRect(root, "selection", {
                x: 0,
                y: 0,
                width: root.clientWidth || 0,
                height: root.clientHeight || 0
            });
        }
        requestCanvasRedraw(root, "external-formula-session", "selection");
    };

    window.tmSpreadsheetCanvas.clearFormulaReferenceHighlights = function (root) {
        const s = getState(root);
        if (!s?.sheetState) return;
        clearExternalFormulaPicker(root);
        s.sheetState.externalFormulaOrigin = null;
        s.formulaReferenceSource = null;
        s.formulaReferenceRevision = 0;
        s.formulaReferenceCells = [];
        if (s.metrics) {
            s.metrics.formulaEditorHighlightCount = 0;
            s.metrics.formulaEditorReferenceCount = 0;
        }
        addDirtyRect(root, "selection", {
            x: 0,
            y: 0,
            width: root.clientWidth || 0,
            height: root.clientHeight || 0
        });
        requestCanvasRedraw(root, "clear-formula-reference-highlights", "selection");
    };

    window.tmSpreadsheetCanvas.setSearchHighlights = function (root, payload) {
        const s = getState(root);
        if (!s) return;
        const cells = (payload && payload.cells) || [];
        const set = new Set();
        for (const ref of cells) {
            const parsed = parseCellRef(ref);
            if (parsed) set.add(`${parsed.row}|${parsed.col}`);
        }
        const active = payload && payload.active ? parseCellRef(payload.active) : null;
        s.searchHighlights = { set, active: active ? `${active.row}|${active.col}` : null };
        addDirtyRect(root, "selection", { x: 0, y: 0, width: root.clientWidth || 0, height: root.clientHeight || 0 });
        requestCanvasRedraw(root, "set-search-highlights", "selection");
    };

    window.tmSpreadsheetCanvas.clearSearchHighlights = function (root) {
        const s = getState(root);
        if (!s) return;
        s.searchHighlights = null;
        addDirtyRect(root, "selection", { x: 0, y: 0, width: root.clientWidth || 0, height: root.clientHeight || 0 });
        requestCanvasRedraw(root, "clear-search-highlights", "selection");
    };

    window.tmSpreadsheetCanvas.openEditorAtActive = function (root) {
        openLocalEditorAtActive(root);
    };

    window.tmSpreadsheetCanvas.getDebugMetrics = function (root) {
        const s = getState(root);
        if (s?.metrics) {
            s.metrics.keyboardRepeatAccelerationEnabled = keyboardRepeatAccelerationEnabled(root);
            s.metrics.logicalScrollLeft = getLogicalScrollLeft(root);
            s.metrics.logicalScrollTop = getLogicalScrollTop(root);
            s.metrics.nativeScrollLeft = root.scrollLeft || 0;
            s.metrics.nativeScrollTop = root.scrollTop || 0;
            s.metrics.localRevision = s.sheetState?.localRevision || 0;
            s.metrics.blazorRevision = s.sheetState?.blazorRevision || 0;
            s.metrics.serverRevision = s.sheetState?.serverRevision || 0;
            const store = s.sheetState?.cellStore;
            if (store) {
                s.metrics.cellStoreSize = store.cells.size;
                s.metrics.cellStoreRevision = store.revision;
                s.metrics.cellStoreLastFrameCellCount = store.lastFrameCellCount;
                s.metrics.cellStoreFormulaRefCount = store.formulaRefs.size;
                s.metrics.cellStoreStyledOrNonEmptyCount = store.styledOrNonEmpty.size;
                s.metrics.cellStoreMergedCount = store.merged.size;
            }
        }
        return s?.metrics
            ? {
                ...s.metrics,
                dotNetCallbacksByMethod: { ...(s.metrics.dotNetCallbacksByMethod || {}) },
                hotPathDotNetCallbacksByMethod: { ...(s.metrics.hotPathDotNetCallbacksByMethod || {}) },
                workbookState: s.workbookState
                    ? {
                        localRevision: s.workbookState.localRevision || 0,
                        blazorRevision: s.workbookState.blazorRevision || 0,
                        serverRevision: s.workbookState.serverRevision || 0,
                        activeSheetId: s.workbookState.activeSheetId || ""
                    }
                    : null,
                sheetState: s.sheetState
                    ? {
                        localRevision: s.sheetState.localRevision || 0,
                        blazorRevision: s.sheetState.blazorRevision || 0,
                        serverRevision: s.sheetState.serverRevision || 0,
                        activeCell: { ...(s.sheetState.activeCell || {}) },
                        selection: { ...(s.sheetState.selection || {}) },
                        scroll: { ...(s.sheetState.scroll || {}) },
                        hover: s.sheetState.hover ? { ...s.sheetState.hover } : null,
                        drag: {
                            possible: s.sheetState.drag?.possible ? { ...s.sheetState.drag.possible } : null,
                            selection: s.sheetState.drag?.selection ? { ...s.sheetState.drag.selection } : null,
                            autoscrollPointer: s.sheetState.drag?.autoscrollPointer ? { ...s.sheetState.drag.autoscrollPointer } : null
                        },
                        resize: s.resize
                            ? {
                                kind: s.resize.kind || "",
                                index: s.resize.index || 0,
                                size: s.resize.size || 0,
                                currentSize: s.resize.currentSize || 0
                            }
                            : null,
                        cellStore: {
                            size: s.sheetState.cellStore?.cells?.size || 0,
                            revision: s.sheetState.cellStore?.revision || 0,
                            formulaRefCount: s.sheetState.cellStore?.formulaRefs?.size || 0,
                            styledOrNonEmptyCount: s.sheetState.cellStore?.styledOrNonEmpty?.size || 0,
                            mergedCount: s.sheetState.cellStore?.merged?.size || 0,
                            lastFrameCellCount: s.sheetState.cellStore?.lastFrameCellCount || 0
                        },
                        editor: s.sheetState.editor ? { ...s.sheetState.editor } : null,
                        formulaEditor: s.sheetState.formulaEditor
                            ? {
                                active: !!s.sheetState.formulaEditor.active,
                                row: s.sheetState.formulaEditor.row || 0,
                                col: s.sheetState.formulaEditor.col || 0,
                                text: s.sheetState.formulaEditor.text || "",
                                caret: s.sheetState.formulaEditor.caret || 0,
                                selectionStart: s.sheetState.formulaEditor.selectionStart ?? 0,
                                selectionEnd: s.sheetState.formulaEditor.selectionEnd ?? 0,
                                tokenStart: s.sheetState.formulaEditor.tokenStart ?? -1,
                                tokenEnd: s.sheetState.formulaEditor.tokenEnd ?? -1,
                                caretTokenStart: s.sheetState.formulaEditor.caretTokenStart ?? -1,
                                caretTokenEnd: s.sheetState.formulaEditor.caretTokenEnd ?? -1,
                                selectionTokenStart: s.sheetState.formulaEditor.selectionTokenStart ?? -1,
                                selectionTokenEnd: s.sheetState.formulaEditor.selectionTokenEnd ?? -1,
                                activeTokenIndex: s.sheetState.formulaEditor.activeTokenIndex ?? -1,
                                refCount: s.sheetState.formulaEditor.refs?.length || 0,
                                dragActive: !!s.sheetState.formulaEditor.dragAnchor
                            }
                            : null,
                        formulaMode: !!s.sheetState.formulaMode,
                        formatPainterActive: !!s.sheetState.formatPainterActive
                    }
                    : null
            }
            : null;
    };

    window.tmSpreadsheetCanvas.ensureCellVisible = function (root, cell, options) {
        if (!root || !cell) return;
        const rowHeaderWidth = read(options, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(options, "ColumnHeaderHeight", 20);
        const frozenRow = read(cell, "FrozenRow", false);
        const frozenColumn = read(cell, "FrozenColumn", false);
        const left = read(cell, "Left", 0);
        const right = read(cell, "Right", left);
        const top = read(cell, "Top", 0);
        const bottom = read(cell, "Bottom", top);
        let nextLeft = getLogicalScrollLeft(root);
        let nextTop = getLogicalScrollTop(root);

        if (!frozenColumn) {
            const visibleLeft = nextLeft + rowHeaderWidth;
            const visibleRight = nextLeft + root.clientWidth;
            if (left < visibleLeft) nextLeft = left - rowHeaderWidth;
            else if (right > visibleRight) nextLeft = right - root.clientWidth;
        }

        if (!frozenRow) {
            const visibleTop = nextTop + columnHeaderHeight;
            const visibleBottom = nextTop + root.clientHeight;
            if (top < visibleTop) nextTop = top - columnHeaderHeight;
            else if (bottom > visibleBottom) nextTop = bottom - root.clientHeight;
        }

        const s = getState(root);
        if (s?.metrics) {
            s.metrics.scrollToCount += 1;
            s.metrics.programmaticEnsureScrollToCount += 1;
        }
        if (s) {
            setLogicalScroll(root, nextLeft, nextTop, "programmatic");
            syncModelViewport(root, s.model || {});
            requestPaint(root, "programmatic-scroll", "full");
            syncNativeScrollFromLogical(root);
            requestViewportSync(root, true);
        } else {
            root.scrollTo({ left: Math.max(0, nextLeft), top: Math.max(0, nextTop), behavior: "auto" });
        }
    };

    function prepareRenderCaches(root) {
        const s = getState(root);
        if (!s) return;

        const nextSignature = getFontSignature(root);
        if (s.fontSignature && s.fontSignature !== nextSignature) {
            s.textMetricsCache?.clear();
            s.fontStringCache?.clear();
        }

        s.fontSignature = nextSignature;
    }

    function getFontSignature(root) {
        const computed = getComputedStyle(root);
        return [
            computed.fontFamily,
            computed.fontSize,
            css(root, "--tm-font-sans", ""),
            css(root, "--tm-font-mono", "")
        ].join("|");
    }

    function updateCacheMetrics(root, layout) {
        const s = getState(root);
        const metrics = s?.metrics;
        if (!metrics) return;

        metrics.textMeasureCacheSize = s.textMetricsCache?.size || 0;
        metrics.fontStringCacheSize = s.fontStringCache?.size || 0;
        metrics.paintStyleCacheSize = s.paintStyleCache?.size || 0;
        metrics.displayValueCacheSize = s.displayValueCache?.size || 0;
        metrics.cellSnapshotCacheSize = s.cellSnapshotCache?.size || 0;
        if (layout) {
            metrics.visibleRowCount = layout.rows.length;
            metrics.visibleColumnCount = layout.columns.length;
        }
    }

    function resetFrameInstrumentation(metrics) {
        if (!metrics) return;
        metrics.lastDrawCellsMs = 0;
        metrics.lastDrawCellContentMs = 0;
        metrics.lastContextSaveClipRestoreCount = 0;
        metrics.lastContextSaveMs = 0;
        metrics.lastContextClipMs = 0;
        metrics.lastContextRestoreMs = 0;
    }

    function resetContextState(ctx) {
        ctx.__tmState = {
            font: null,
            fillStyle: null,
            strokeStyle: null,
            lineWidth: null,
            textBaseline: null,
            textAlign: null
        };
    }

    function getContextState(ctx) {
        return ctx.__tmState || (ctx.__tmState = {});
    }

    function setContextProperty(ctx, property, value, metrics) {
        const state = getContextState(ctx);
        if (state[property] === value) {
            if (metrics) metrics.contextStateSkipCount += 1;
            return;
        }

        ctx[property] = value;
        state[property] = value;
        if (metrics) metrics.contextStateSetCount += 1;
    }

    function setContextFont(ctx, value, metrics) {
        setContextProperty(ctx, "font", value, metrics);
    }

    function setContextFillStyle(ctx, value, metrics) {
        setContextProperty(ctx, "fillStyle", value, metrics);
    }

    function setContextStrokeStyle(ctx, value, metrics) {
        setContextProperty(ctx, "strokeStyle", value, metrics);
    }

    function setContextLineWidth(ctx, value, metrics) {
        setContextProperty(ctx, "lineWidth", value, metrics);
    }

    function setContextTextBaseline(ctx, value, metrics) {
        setContextProperty(ctx, "textBaseline", value, metrics);
    }

    function setContextTextAlign(ctx, value, metrics) {
        setContextProperty(ctx, "textAlign", value, metrics);
    }

    function contextSave(ctx, metrics) {
        const started = performance.now();
        ctx.save();
        if (metrics) {
            const elapsed = performance.now() - started;
            metrics.lastContextSaveClipRestoreCount += 1;
            metrics.contextSaveClipRestoreTotalCount += 1;
            metrics.lastContextSaveMs += elapsed;
            metrics.contextSaveTotalMs += elapsed;
        }
    }

    function contextClip(ctx, metrics) {
        const started = performance.now();
        ctx.clip();
        if (metrics) {
            const elapsed = performance.now() - started;
            metrics.lastContextSaveClipRestoreCount += 1;
            metrics.contextSaveClipRestoreTotalCount += 1;
            metrics.lastContextClipMs += elapsed;
            metrics.contextClipTotalMs += elapsed;
        }
    }

    function contextRestore(ctx, metrics) {
        const started = performance.now();
        ctx.restore();
        resetContextState(ctx);
        if (metrics) {
            const elapsed = performance.now() - started;
            metrics.lastContextSaveClipRestoreCount += 1;
            metrics.contextSaveClipRestoreTotalCount += 1;
            metrics.lastContextRestoreMs += elapsed;
            metrics.contextRestoreTotalMs += elapsed;
        }
    }

    function cellSnapshotId(row, col) {
        return row + ":" + col;
    }

    function cellSnapshotKey(root, cell, style, paint, displayValue, imageUrl) {
        const borders = [
            borderSnapshotKey(read(style, "BorderTop", null)),
            borderSnapshotKey(read(style, "BorderRight", null)),
            borderSnapshotKey(read(style, "BorderBottom", null)),
            borderSnapshotKey(read(style, "BorderLeft", null))
        ].join("/");

        return [
            read(cell, "Value", ""),
            displayValue ?? "",
            imageUrl || "",
            getCanvasFont(root, style),
            paint.foreColor || "",
            paint.backgroundColor || "",
            paint.horizontalAlignValue || "",
            paint.verticalAlignValue || "",
            paint.underline ? 1 : 0,
            paint.doubleUnderline ? 1 : 0,
            paint.strikeThrough ? 1 : 0,
            paint.hyperlink ? 1 : 0,
            borders
        ].join("\u001f");
    }

    function borderSnapshotKey(border) {
        if (!border) return "";
        return [
            read(border, "Style", "none"),
            read(border, "Color", "")
        ].join(":");
    }

    function clearCellSnapshots(root, reason) {
        const s = getState(root);
        if (!s?.cellSnapshotCache) return;
        const removed = s.cellSnapshotCache.size;
        if (!removed) return;
        s.cellSnapshotCache.clear();
        if (s.metrics) s.metrics.cellSnapshotInvalidationCount += removed;
    }

    function invalidateCellSnapshot(root, row, col) {
        const s = getState(root);
        if (!s?.cellSnapshotCache) return;
        if (s.cellSnapshotCache.delete(cellSnapshotId(row, col)) && s.metrics) {
            s.metrics.cellSnapshotInvalidationCount += 1;
        }
    }

    function invalidateColumnSnapshots(root, col) {
        const s = getState(root);
        if (!s?.cellSnapshotCache) return;
        let removed = 0;
        for (const key of [...s.cellSnapshotCache.keys()]) {
            if (Number(key.split(":")[1]) === col) {
                s.cellSnapshotCache.delete(key);
                removed++;
            }
        }

        if (removed && s.metrics) s.metrics.cellSnapshotInvalidationCount += removed;
    }

    function invalidateRowSnapshots(root, row) {
        const s = getState(root);
        if (!s?.cellSnapshotCache) return;
        let removed = 0;
        for (const key of [...s.cellSnapshotCache.keys()]) {
            if (Number(key.split(":")[0]) === row) {
                s.cellSnapshotCache.delete(key);
                removed++;
            }
        }

        if (removed && s.metrics) s.metrics.cellSnapshotInvalidationCount += removed;
    }

    function pruneCellSnapshots(root, visibleIds) {
        const s = getState(root);
        if (!s?.cellSnapshotCache || !visibleIds) return;
        let removed = 0;
        for (const key of [...s.cellSnapshotCache.keys()]) {
            if (!visibleIds.has(key)) {
                s.cellSnapshotCache.delete(key);
                removed++;
            }
        }

        if (removed && s.metrics) s.metrics.cellSnapshotInvalidationCount += removed;
    }

    function invalidateCellSnapshotRefs(root, refs) {
        if (!Array.isArray(refs)) return;
        for (const cellRef of refs) {
            const cell = parseCellRef(cellRef);
            invalidateCellSnapshot(root, cell.row, cell.col);
        }
    }

    function invalidateCellSnapshotRows(root, rows) {
        if (!Array.isArray(rows)) return;
        for (const row of rows) {
            const rowIndex = Number(row);
            if (Number.isFinite(rowIndex) && rowIndex >= 0) invalidateRowSnapshots(root, rowIndex);
        }
    }

    function invalidateCellSnapshotColumns(root, columns) {
        if (!Array.isArray(columns)) return;
        for (const col of columns) {
            const colIndex = Number(col);
            if (Number.isFinite(colIndex) && colIndex >= 0) invalidateColumnSnapshots(root, colIndex);
        }
    }

    function getVisibleLayout(root, model, width, height) {
        const s = getState(root);
        const layoutState = getLayoutState(root);
        const rowHeaderWidth = layoutState?.rowHeaderWidth ?? read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = layoutState?.columnHeaderHeight ?? read(model, "ColumnHeaderHeight", 20);
        const freezeRows = layoutState?.freezeRowCount ?? read(model, "FreezeRowCount", 0);
        const freezeColumns = layoutState?.freezeColumnCount ?? read(model, "FreezeColumnCount", 0);
        const key = [
            Math.round(read(model, "ScrollLeft", 0) * 100) / 100,
            Math.round(read(model, "ScrollTop", 0) * 100) / 100,
            Math.round(width),
            Math.round(height),
            // The render scale is part of the layout: a pinch (or the status-bar slider) changes the zoom
            // without touching the scroll position or the viewport size, and a cache hit would then paint
            // the OLD row heights / column widths — text clipped at the pre-zoom widths and offset.
            Math.round(positiveOr(read(model, "Zoom", 1), 1) * 1000) / 1000,
            Math.round(positiveOr(read(model, "DensityRowScale", 1), 1) * 1000) / 1000,
            rowHeaderWidth,
            columnHeaderHeight,
            freezeRows,
            freezeColumns,
            layoutState?.rowRevision || 0,
            layoutState?.columnRevision || 0,
            layoutOverscanRows(),
            layoutOverscanColumns()
        ].join(";");

        if (s?.visibleLayoutCache?.key === key) {
            if (s.metrics) s.metrics.visibleLayoutCacheHits += 1;
            return s.visibleLayoutCache.layout;
        }

        const visible = {
            rows: [],
            columns: [],
            rowHeaderWidth,
            columnHeaderHeight,
            width,
            height
        };

        if (layoutState) {
            const rowOffsets = getRowOffsets(layoutState);
            const columnOffsets = getColumnOffsets(layoutState);
            const scrollTop = read(model, "ScrollTop", 0);
            const scrollLeft = read(model, "ScrollLeft", 0);
            const bodyHeight = Math.max(0, height - columnHeaderHeight);
            const bodyWidth = Math.max(0, width - rowHeaderWidth);
            const rowOverscan = layoutOverscanRows();
            const columnOverscan = layoutOverscanColumns();
            const seenRows = new Set();
            const seenColumns = new Set();

            for (let row = 0; row < Math.min(freezeRows, layoutState.rowCount); row++) {
                visible.rows.push(createRowFrame(layoutState, row, scrollTop, true));
                seenRows.add(row);
            }

            let startRow = binarySearchOffset(rowOffsets, scrollTop);
            let endRow = binarySearchOffset(rowOffsets, scrollTop + bodyHeight);
            if (s?.metrics) s.metrics.visibleLayoutBinarySearchCount += 2;
            startRow = Math.max(0, startRow - rowOverscan);
            endRow = Math.min(layoutState.rowCount - 1, endRow + rowOverscan);
            for (let row = startRow; row <= endRow; row++) {
                if (seenRows.has(row)) continue;
                const frame = createRowFrame(layoutState, row, scrollTop, false);
                if (frame.height <= 0 || frame.y + frame.height < columnHeaderHeight || frame.y > height) continue;
                visible.rows.push(frame);
                seenRows.add(row);
            }

            for (let col = 0; col < Math.min(freezeColumns, layoutState.columnCount); col++) {
                visible.columns.push(createColumnFrame(layoutState, col, scrollLeft, true));
                seenColumns.add(col);
            }

            let startCol = binarySearchOffset(columnOffsets, scrollLeft);
            let endCol = binarySearchOffset(columnOffsets, scrollLeft + bodyWidth);
            if (s?.metrics) s.metrics.visibleLayoutBinarySearchCount += 2;
            startCol = Math.max(0, startCol - columnOverscan);
            endCol = Math.min(layoutState.columnCount - 1, endCol + columnOverscan);
            for (let col = startCol; col <= endCol; col++) {
                if (seenColumns.has(col)) continue;
                const frame = createColumnFrame(layoutState, col, scrollLeft, false);
                if (frame.width <= 0 || frame.x + frame.width < rowHeaderWidth || frame.x > width) continue;
                visible.columns.push(frame);
                seenColumns.add(col);
            }

            if (s?.metrics) s.metrics.visibleLayoutJsComputeCount += 1;
        } else {
            const rows = read(model, "Rows", []);
            const columns = read(model, "Columns", []);
            for (const row of rows) {
                const y = screenY(model, row);
                const rowHeight = read(row, "Height", 0);
                if (rowHeight <= 0 || y + rowHeight < columnHeaderHeight || y > height) continue;
                visible.rows.push({
                    source: row,
                    index: read(row, "Index", 0),
                    Index: read(row, "Index", 0),
                    top: read(row, "Top", 0),
                    Top: read(row, "Top", 0),
                    y,
                    height: rowHeight,
                    Height: rowHeight
                });
            }

            for (const col of columns) {
                const x = screenX(model, col);
                const colWidth = read(col, "Width", 0);
                if (colWidth <= 0 || x + colWidth < rowHeaderWidth || x > width) continue;
                visible.columns.push({
                    source: col,
                    index: read(col, "Index", 0),
                    Index: read(col, "Index", 0),
                    left: read(col, "Left", 0),
                    Left: read(col, "Left", 0),
                    label: read(col, "Label", ""),
                    Label: read(col, "Label", ""),
                    x,
                    width: colWidth,
                    Width: colWidth
                });
            }
        }

        if (s) {
            s.visibleLayoutCache = { key, layout: visible };
            if (s.metrics) s.metrics.visibleLayoutCacheMisses += 1;
        }

        return visible;
    }

    function frameSignature(items, indexName, offsetName, sizeName) {
        if (!items || items.length === 0) return "0";
        let signature = String(items.length);
        for (const item of items) {
            signature += "|" + read(item, indexName, 0)
                + ":" + Math.round(read(item, offsetName, 0) * 100) / 100
                + ":" + Math.round(read(item, sizeName, 0) * 100) / 100
                + ":" + (read(item, "Frozen", false) ? 1 : 0);
        }
        return signature;
    }

    function renderContentLayer(root, model, options) {
        const started = performance.now();
        const settings = options || {};
        const s = getState(root);
        const metrics = s?.metrics;
        const canvas = settings.canvas || s?.canvas;
        if (!canvas || !model) return { cells: 0, texts: 0, textMs: 0 };

        if (settings.resetFrame !== false) resetFrameInstrumentation(metrics);
        prepareRenderCaches(root);
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        resizeCanvasSurface(root, canvas, width, height, dpr);

        const ctx = canvas.getContext("2d");
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        resetContextState(ctx);

        const palette = s?.palette || buildPalette(root);
        const dirtyRect = settings.forceFull ? null : unionRects(settings.dirtyRects, width, height);
        let clipped = false;
        if (dirtyRect) {
            contextSave(ctx, metrics);
            ctx.beginPath();
            ctx.rect(dirtyRect.x, dirtyRect.y, dirtyRect.width, dirtyRect.height);
            contextClip(ctx, metrics);
            ctx.clearRect(dirtyRect.x, dirtyRect.y, dirtyRect.width, dirtyRect.height);
            setContextFillStyle(ctx, palette.surface, metrics);
            ctx.fillRect(dirtyRect.x, dirtyRect.y, dirtyRect.width, dirtyRect.height);
            clipped = true;
        } else {
            ctx.clearRect(0, 0, width, height);
            setContextFillStyle(ctx, palette.surface, metrics);
            ctx.fillRect(0, 0, width, height);
        }

        const layout = getVisibleLayout(root, model, width, height);
        const cellMetrics = drawCells(ctx, root, model, palette);
        if (clipped) contextRestore(ctx, metrics);

        if (metrics) {
            const elapsed = performance.now() - started;
            metrics.contentLayerPaintCount += 1;
            metrics.lastContentLayerMs = elapsed;
            updateCacheMetrics(root, layout);
            if (elapsed > 33) metrics.contentFramesOver33 += 1;
            if (elapsed > 16) metrics.contentFramesOver16 += 1;
        }

        return cellMetrics;
    }

    function renderModel(root, canvas, model) {
        const started = performance.now();
        const s = getState(root);
        const metrics = s?.metrics;
        if (s) modelRootMap.set(model, root);
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        if (s?.headerCanvas) resizeCanvasSurface(root, s.headerCanvas, width, height, dpr);
        if (s?.selectionCanvas) resizeCanvasSurface(root, s.selectionCanvas, width, height, dpr);
        resetFrameInstrumentation(metrics);
        const layout = getVisibleLayout(root, model, width, height);
        const cellMetrics = renderContentLayer(root, model, { canvas, forceFull: true, resetFrame: false });
        if (s?.headerCanvas) renderHeaderLayer(root, model);
        else {
            const ctx = canvas.getContext("2d");
            drawHeaders(ctx, root, model, s?.palette || buildPalette(root), width, height);
        }
        if (s?.selectionCanvas) renderSelectionOverlay(root, model);
        else {
            const ctx = canvas.getContext("2d");
            drawSelection(ctx, root, model, s?.palette || buildPalette(root), width, height);
        }
        if (metrics) {
            metrics.redrawCount += 1;
            metrics.lastDrawMs = performance.now() - started;
            metrics.lastTextDrawMs = cellMetrics.textMs;
            metrics.lastVisibleCellCount = cellMetrics.cells;
            metrics.lastTextCount = cellMetrics.texts;
            updateCacheMetrics(root, layout);
            if (metrics.lastDrawMs > 33) metrics.slowFramesOver33 += 1;
            if (metrics.lastDrawMs > 16) metrics.slowFramesOver16 += 1;
        }
        updateLocalEditorPosition(root);
        syncEditorAccessibility(root);
        syncAccessibilityState(root, false);
    }

    function tryBitmapScrollRedraw(root, model, oldScrollLeft, oldScrollTop) {
        const s = getState(root);
        const canvas = s?.canvas;
        if (!s || !canvas || !model) return false;

        const started = performance.now();
        const metrics = s.metrics;
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        const pixelWidth = Math.round(width * dpr);
        const pixelHeight = Math.round(height * dpr);
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const bodyWidth = Math.max(0, width - rowHeaderWidth);
        const bodyHeight = Math.max(0, height - columnHeaderHeight);
        resetFrameInstrumentation(metrics);

        const fallback = reason => {
            if (metrics) {
                metrics.bitmapShiftFallbackCount += 1;
                metrics.lastBitmapShiftReason = reason;
            }
            return false;
        };

        if (read(model, "FreezeRowCount", 0) > 0 || read(model, "FreezeColumnCount", 0) > 0) {
            return fallback("frozen");
        }

        if (bodyWidth <= 0 || bodyHeight <= 0) return fallback("empty-body");
        if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) return fallback("resize-or-dpr");

        const newScrollLeft = getLogicalScrollLeft(root);
        const newScrollTop = getLogicalScrollTop(root);
        const rawDx = oldScrollLeft - newScrollLeft;
        const rawDy = oldScrollTop - newScrollTop;
        const dx = Math.round(rawDx);
        const dy = Math.round(rawDy);
        if (Math.abs(dx) < 1 && Math.abs(dy) < 1) return fallback("no-delta");
        if (Math.abs(dx) >= bodyWidth || Math.abs(dy) >= bodyHeight) return fallback("full-viewport");
        if (Math.abs(dx) > Math.max(96, bodyWidth * 0.35) || Math.abs(dy) > Math.max(96, bodyHeight * 0.35)) {
            return fallback("large-delta");
        }

        syncModelViewport(root, model);
        prepareRenderCaches(root);

        const palette = s.palette || buildPalette(root);
        resizeCanvasSurface(root, canvas, width, height, dpr);
        const ctx = canvas.getContext("2d");
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        resetContextState(ctx);
        contextSave(ctx, metrics);
        ctx.beginPath();
        ctx.rect(rowHeaderWidth, columnHeaderHeight, bodyWidth, bodyHeight);
        contextClip(ctx, metrics);
        ctx.drawImage(
            canvas,
            rowHeaderWidth,
            columnHeaderHeight,
            bodyWidth,
            bodyHeight,
            rowHeaderWidth + dx,
            columnHeaderHeight + dy,
            bodyWidth,
            bodyHeight);

        const strips = getExposedScrollStrips(rowHeaderWidth, columnHeaderHeight, bodyWidth, bodyHeight, dx, dy);
        let cellMetrics = { cells: 0, texts: 0, textMs: 0 };
        for (const strip of strips) {
            contextSave(ctx, metrics);
            ctx.beginPath();
            ctx.rect(strip.x, strip.y, strip.width, strip.height);
            contextClip(ctx, metrics);
            setContextFillStyle(ctx, palette.surface, metrics);
            ctx.fillRect(strip.x, strip.y, strip.width, strip.height);
            cellMetrics = addCellMetrics(cellMetrics, drawCells(ctx, root, model, palette, { allowSnapshotSkip: false, storeSnapshots: false }));
            contextRestore(ctx, metrics);
        }

        contextRestore(ctx, metrics);
        if (s.headerCanvas) renderHeaderLayer(root, model);
        if (s.selectionCanvas) renderSelectionOverlay(root, model);

        if (metrics) {
            metrics.bitmapShiftCount += 1;
            metrics.lastBitmapShiftDx = dx;
            metrics.lastBitmapShiftDy = dy;
            metrics.lastBitmapShiftReason = "bitmap-shift";
            metrics.lastDrawMs = performance.now() - started;
            metrics.lastTextDrawMs = cellMetrics.textMs;
            metrics.lastVisibleCellCount = cellMetrics.cells;
            metrics.lastTextCount = cellMetrics.texts;
            updateCacheMetrics(root, getVisibleLayout(root, model, width, height));
            if (metrics.lastDrawMs > 33) metrics.slowFramesOver33 += 1;
            if (metrics.lastDrawMs > 16) metrics.slowFramesOver16 += 1;
        }

        return true;
    }

    function getExposedScrollStrips(x, y, width, height, dx, dy) {
        const strips = [];
        if (dy > 0) {
            strips.push({ x, y, width, height: Math.min(height, dy) });
        } else if (dy < 0) {
            const h = Math.min(height, -dy);
            strips.push({ x, y: y + height - h, width, height: h });
        }

        if (dx > 0) {
            strips.push({ x, y, width: Math.min(width, dx), height });
        } else if (dx < 0) {
            const w = Math.min(width, -dx);
            strips.push({ x: x + width - w, y, width: w, height });
        }

        return strips;
    }

    function addCellMetrics(left, right) {
        return {
            cells: left.cells + right.cells,
            texts: left.texts + right.texts,
            textMs: left.textMs + right.textMs
        };
    }

    function renderHeaderLayer(root, model, options) {
        const s = getState(root);
        const canvas = s?.headerCanvas;
        if (!s || !canvas || !model) return;

        const started = performance.now();
        const settings = options || {};
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        resizeCanvasSurface(root, canvas, width, height, dpr);

        const ctx = canvas.getContext("2d");
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        resetContextState(ctx);
        const rects = settings.forceFull ? [] : normalizeDirtyRects(settings.dirtyRects, width, height);
        if (rects.length > 0) {
            // Every dirty rect is cleared and the union of them clipped in ONE pass: the header highlight
            // touches the old and the new row/column strip of a selection, which are usually far apart,
            // and a single bounding rect between them would repaint (almost) the whole layer.
            for (const rect of rects) ctx.clearRect(rect.x, rect.y, rect.width, rect.height);
            contextSave(ctx, s.metrics);
            ctx.beginPath();
            for (const rect of rects) ctx.rect(rect.x, rect.y, rect.width, rect.height);
            contextClip(ctx, s.metrics);
            drawHeaders(ctx, root, model, s.palette || buildPalette(root), width, height);
            contextRestore(ctx, s.metrics);
        } else {
            ctx.clearRect(0, 0, width, height);
            drawHeaders(ctx, root, model, s.palette || buildPalette(root), width, height);
        }
        if (s.metrics) {
            s.metrics.headerLayerPaintCount += 1;
            s.metrics.lastHeaderLayerMs = performance.now() - started;
        }
    }

    function resizeCanvasSurface(root, canvas, width, height, dpr) {
        canvas.style.width = width + "px";
        canvas.style.height = height + "px";
        const layer = canvas.parentElement;
        if (layer && layer.classList.contains("tm-spreadsheet-canvas-grid__layer")) {
            layer.style.width = width + "px";
            layer.style.height = height + "px";
        }

        const pixelWidth = Math.round(width * dpr);
        const pixelHeight = Math.round(height * dpr);
        if (canvas.width !== pixelWidth || canvas.height !== pixelHeight) {
            canvas.width = pixelWidth;
            canvas.height = pixelHeight;
        }
    }

    function renderSelectionOverlay(root, model, options) {
        const s = getState(root);
        const canvas = s?.selectionCanvas;
        if (!s || !canvas || !model) return;

        const started = performance.now();
        const settings = options || {};
        const dpr = window.devicePixelRatio || 1;
        const width = Math.max(1, root.clientWidth || read(model, "ViewportWidth", 1));
        const height = Math.max(1, root.clientHeight || read(model, "ViewportHeight", 1));
        resizeCanvasSurface(root, canvas, width, height, dpr);

        const ctx = canvas.getContext("2d");
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        resetContextState(ctx);
        const dirtyRect = settings.forceFull ? null : unionRects(settings.dirtyRects, width, height);
        let clipped = false;
        if (dirtyRect) {
            ctx.clearRect(dirtyRect.x, dirtyRect.y, dirtyRect.width, dirtyRect.height);
            contextSave(ctx, s.metrics);
            ctx.beginPath();
            ctx.rect(dirtyRect.x, dirtyRect.y, dirtyRect.width, dirtyRect.height);
            contextClip(ctx, s.metrics);
            clipped = true;
        } else {
            ctx.clearRect(0, 0, width, height);
        }

        const palette = s.palette || buildPalette(root);
        drawSelection(ctx, root, model, palette, width, height);
        if (clipped) contextRestore(ctx, s.metrics);
        // Phase 7: the DOM touch handles follow the painted selection (grid coordinates) — they are
        // real elements so `touch-action: none` can stop the browser panning under a finger that
        // grabbed a handle; on a fine pointer CSS hides the whole layer.
        syncTouchHandles(root, model);
        if (s.metrics) {
            s.metrics.selectionRedrawCount += 1;
            const elapsed = performance.now() - started;
            s.metrics.selectionLayerPaintCount += 1;
            s.metrics.lastSelectionDrawMs = elapsed;
            s.metrics.lastSelectionLayerMs = elapsed;
            if (elapsed > 33) s.metrics.selectionFramesOver33 += 1;
            if (elapsed > 16) s.metrics.selectionFramesOver16 += 1;
            updateCacheMetrics(root, getVisibleLayout(root, model, width, height));
        }
    }

    function drawHeaders(ctx, root, model, palette, width, height) {
        const metrics = getState(root)?.metrics;
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const layout = getVisibleLayout(root, model, width, height);
        // The LOCAL selection snapshot, not the model's copy: during a drag the model lags a frame and
        // the header must highlight the same range the selection layer draws.
        const highlight = resolveHeaderHighlight(getSelectionSnapshot(root));

        setContextFillStyle(ctx, palette.headerBg, metrics);
        ctx.fillRect(0, 0, width, columnHeaderHeight);
        ctx.fillRect(0, 0, rowHeaderWidth, height);
        setContextStrokeStyle(ctx, palette.border, metrics);
        ctx.strokeRect(0.5, 0.5, rowHeaderWidth - 0.5, columnHeaderHeight - 0.5);

        // The label scales with the density font scale (the token itself is untouched): 11px labels on
        // a 28px Comfortable gutter would read cramped next to the 12.5pt cells.
        setContextFont(ctx, resolveHeaderFont(palette.headerFont, getDensityScales(root).fontScale), metrics);
        setContextTextAlign(ctx, "center", metrics);
        setContextTextBaseline(ctx, "middle", metrics);

        for (const col of layout.columns) {
            const x = col.x;
            const w = col.width;
            const active = isHighlightedIndex(highlight.activeColumns, read(col, "Index", -1));
            setContextFillStyle(ctx, active ? palette.headerActiveBg : palette.headerBg, metrics);
            ctx.fillRect(x, 0, w, columnHeaderHeight);
            setContextStrokeStyle(ctx, palette.border, metrics);
            ctx.strokeRect(Math.floor(x) + 0.5, 0.5, w, columnHeaderHeight - 0.5);
            setContextFillStyle(ctx, active ? palette.headerActiveText : palette.headerText, metrics);
            ctx.fillText(col.label, x + w / 2, columnHeaderHeight / 2);
            if (active) {
                // The accent rail sits on the grid edge of the gutter, the way the reference marks the
                // active column: the tint alone must not be the only signal (forced-colors, colour vision).
                setContextFillStyle(ctx, palette.selectionBorder, metrics);
                ctx.fillRect(Math.floor(x), columnHeaderHeight - 2, w, 2);
            }
        }

        for (const row of layout.rows) {
            const y = row.y;
            const h = row.height;
            const active = isHighlightedIndex(highlight.activeRows, row.index);
            setContextFillStyle(ctx, active ? palette.headerActiveBg : palette.headerBg, metrics);
            ctx.fillRect(0, y, rowHeaderWidth, h);
            setContextStrokeStyle(ctx, palette.border, metrics);
            ctx.strokeRect(0.5, Math.floor(y) + 0.5, rowHeaderWidth - 0.5, h);
            setContextFillStyle(ctx, active ? palette.headerActiveText : palette.headerText, metrics);
            ctx.fillText(String(row.index + 1), rowHeaderWidth / 2, y + h / 2);
            if (active) {
                setContextFillStyle(ctx, palette.selectionBorder, metrics);
                ctx.fillRect(rowHeaderWidth - 2, Math.floor(y), 2, h);
            }
        }
    }

    function drawCells(ctx, root, model, palette, options) {
        const drawStarted = performance.now();
        const s = getState(root);
        const debugMetrics = s?.metrics;
        const settings = options || {};
        const allowSnapshotSkip = settings.allowSnapshotSkip !== false;
        const storeSnapshots = settings.storeSnapshots !== false;
        const snapshotCache = s?.cellSnapshotCache;
        const visibleSnapshotIds = snapshotCache && storeSnapshots ? new Set() : null;
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const width = root.clientWidth;
        const height = root.clientHeight;
        const showGridLines = read(model, "ShowGridLines", true);
        const scrollLeft = read(model, "ScrollLeft", 0);
        const scrollTop = read(model, "ScrollTop", 0);
        const freezeRows = read(model, "FreezeRowCount", 0);
        const freezeCols = read(model, "FreezeColumnCount", 0);
        const metrics = { cells: 0, texts: 0, textMs: 0 };
        const layout = getVisibleLayout(root, model, width, height);
        const cells = getDrawableCells(root, model, layout);

        for (const cell of cells) {
            const row = read(cell, "Row", 0);
            const col = read(cell, "Col", 0);
            const frozenCol = col < freezeCols;
            const frozenRow = row < freezeRows;
            const x = rowHeaderWidth + read(cell, "Left", 0) - (frozenCol ? 0 : scrollLeft);
            const y = columnHeaderHeight + read(cell, "Top", 0) - (frozenRow ? 0 : scrollTop);
            const w = read(cell, "Width", 0);
            const h = read(cell, "Height", 0);
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height || w <= 0 || h <= 0) continue;
            metrics.cells += 1;

            const style = read(cell, "Style", {});
            const paint = getCellPaint(root, cell, style, palette);
            const imageUrl = read(cell, "ImageUrl", null);
            const value = imageUrl ? null : getDisplayValue(root, cell);
            const hasBackground = !!paint.backgroundColor && paint.backgroundColor !== palette.surface;
            const hasContent = !!imageUrl || (value != null && value !== "");
            if (!hasBackground && !hasContent) {
                if (debugMetrics) debugMetrics.skippedEmptyCellContentCount += 1;
                continue;
            }

            const snapshotId = cellSnapshotId(row, col);
            const snapshotKey = cellSnapshotKey(root, cell, style, paint, value, imageUrl);
            if (visibleSnapshotIds) visibleSnapshotIds.add(snapshotId);

            const textStarted = performance.now();
            let textDrawn = false;
            const snapshot = snapshotCache?.get(snapshotId);
            if (allowSnapshotSkip && canUseCellSnapshot(snapshot, snapshotKey, x, y, w, h)) {
                ctx.drawImage(snapshot.canvas, x, y, w, h);
                textDrawn = snapshot.textDrawn;
                if (debugMetrics) debugMetrics.cellSnapshotHitCount += 1;
            } else {
                if (debugMetrics) debugMetrics.cellSnapshotMissCount += 1;
                if (snapshotCache && storeSnapshots && !imageUrl) {
                    const nextSnapshot = renderCellSnapshot(root, cell, style, paint, palette, w, h, value, imageUrl);
                    if (nextSnapshot) {
                        nextSnapshot.key = snapshotKey;
                        nextSnapshot.x = x;
                        nextSnapshot.y = y;
                        nextSnapshot.w = w;
                        nextSnapshot.h = h;
                        snapshotCache.set(snapshotId, nextSnapshot);
                        ctx.drawImage(nextSnapshot.canvas, x, y, w, h);
                        textDrawn = nextSnapshot.textDrawn;
                        if (debugMetrics) debugMetrics.cellSnapshotStoreCount += 1;
                    } else {
                        textDrawn = drawCellBody(ctx, root, cell, style, paint, palette, x, y, w, h, value, imageUrl);
                    }
                } else {
                    textDrawn = drawCellBody(ctx, root, cell, style, paint, palette, x, y, w, h, value, imageUrl);
                }
            }

            if (textDrawn) {
                metrics.texts += 1;
            }
            metrics.textMs += performance.now() - textStarted;
        }

        pruneCellSnapshots(root, visibleSnapshotIds);

        if (showGridLines) {
            drawGridLines(ctx, root, model, palette, width, height, layout);
        }

        for (const cell of cells) {
            const row = read(cell, "Row", 0);
            const col = read(cell, "Col", 0);
            const frozenCol = col < freezeCols;
            const frozenRow = row < freezeRows;
            const x = rowHeaderWidth + read(cell, "Left", 0) - (frozenCol ? 0 : scrollLeft);
            const y = columnHeaderHeight + read(cell, "Top", 0) - (frozenRow ? 0 : scrollTop);
            const w = read(cell, "Width", 0);
            const h = read(cell, "Height", 0);
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height || w <= 0 || h <= 0) continue;

            const style = read(cell, "Style", {});
            if (!hasBorders(style)) continue;

            drawBorders(ctx, root, style, x, y, w, h, palette);
        }

        drawFilterButtons(ctx, root, model, palette, cells);
        drawValidationDropButtons(ctx, root, model, palette, cells);
        drawValidationCircles(ctx, root, model, palette);

        const drawMs = performance.now() - drawStarted;
        if (debugMetrics) {
            debugMetrics.lastDrawCellsMs += drawMs;
            debugMetrics.drawCellsTotalMs += drawMs;
        }
        return metrics;
    }

    function filterButtonRect(x, y, w, h, scale) {
        // Comfortable raises the cap so the funnel stays a touch target on the taller header row.
        const maxSize = Math.round(16 * (Number(scale) > 0 ? Number(scale) : 1));
        const size = Math.min(maxSize, Math.max(11, h - 4), Math.max(0, w - 2));
        return { x: x + w - size - 1, y: y + (h - size) / 2, w: size, h: size };
    }

    function drawFilterButtons(ctx, root, model, palette, cells) {
        const s = getState(root);
        if (!s) return;
        s.filterButtons = [];

        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const width = root.clientWidth;
        const height = root.clientHeight;
        const scrollLeft = read(model, "ScrollLeft", 0);
        const scrollTop = read(model, "ScrollTop", 0);
        const freezeRows = read(model, "FreezeRowCount", 0);
        const freezeCols = read(model, "FreezeColumnCount", 0);

        for (const cell of cells) {
            const filter = read(cell, "Filter", null);
            if (!filter) continue;

            const row = read(cell, "Row", 0);
            const col = read(cell, "Col", 0);
            const frozenCol = col < freezeCols;
            const frozenRow = row < freezeRows;
            const x = rowHeaderWidth + read(cell, "Left", 0) - (frozenCol ? 0 : scrollLeft);
            const y = columnHeaderHeight + read(cell, "Top", 0) - (frozenRow ? 0 : scrollTop);
            const w = read(cell, "Width", 0);
            const h = read(cell, "Height", 0);
            if (w <= 0 || h <= 0) continue;
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height) continue;

            const btn = filterButtonRect(x, y, w, h, getDensityScales(root).handleScale);
            if (btn.w <= 0 || btn.x < rowHeaderWidth || btn.y < columnHeaderHeight) continue;

            const active = !!read(filter, "Active", false);
            ctx.save();
            ctx.fillStyle = active ? palette.primary : palette.elevated;
            ctx.strokeStyle = active ? palette.primary : palette.border;
            ctx.lineWidth = 1;
            ctx.fillRect(btn.x + 0.5, btn.y + 0.5, btn.w - 1, btn.h - 1);
            ctx.strokeRect(btn.x + 0.5, btn.y + 0.5, btn.w - 1, btn.h - 1);

            // funnel glyph (triangle pointing down)
            ctx.fillStyle = active ? palette.inverseText : palette.muted;
            const cx = btn.x + btn.w / 2;
            const cy = btn.y + btn.h / 2;
            ctx.beginPath();
            ctx.moveTo(cx - 3, cy - 2.5);
            ctx.lineTo(cx + 3, cy - 2.5);
            ctx.lineTo(cx + 1, cy + 0.5);
            ctx.lineTo(cx + 1, cy + 2.5);
            ctx.lineTo(cx - 1, cy + 2.5);
            ctx.lineTo(cx - 1, cy + 0.5);
            ctx.closePath();
            ctx.fill();
            ctx.restore();

            s.filterButtons.push({
                col,
                x: btn.x,
                y: btn.y,
                w: btn.w,
                h: btn.h,
                // Phase 7: the painted funnel is 11-22px; a finger gets the touch floor inside its
                // own column and header gutter, while a fine pointer keeps the painted rect.
                hit: resolveFilterButtonHitRect({
                    rect: btn,
                    coarse: read(model, "PointerCoarse", false) === true,
                    headerHeight: columnHeaderHeight,
                    columnWidth: read(colFrame, "Width", btn.w)
                }),
                active
            });
        }
    }

    function hitFilterButton(root, ev) {
        const s = getState(root);
        const buttons = s?.filterButtons;
        if (!buttons || buttons.length === 0) return null;
        const rect = root.getBoundingClientRect();
        const lx = ev.clientX - rect.left;
        const ly = ev.clientY - rect.top;
        for (const b of buttons) {
            // The coarse-pointer hit rect (phase 7) may be larger than the painted funnel; a fine
            // pointer keeps the painted rectangle it always had.
            const hit = b.hit || { x: b.x, y: b.y, width: b.w, height: b.h };
            if (lx >= hit.x && lx <= hit.x + hit.width && ly >= hit.y && ly <= hit.y + hit.height) return b;
        }
        return null;
    }

    function validationDropButtonRect(x, y, w, h, scale) {
        const maxSize = Math.round(16 * (Number(scale) > 0 ? Number(scale) : 1));
        const size = Math.min(maxSize, Math.max(11, h - 4), Math.max(0, w - 2));
        return { x: x + w - size - 1, y: y + (h - size) / 2, w: size, h: size };
    }

    function drawValidationDropButtons(ctx, root, model, palette, cells) {
        const s = getState(root);
        if (!s) return;
        s.validationDropButtons = [];
        if (!cells || cells.length === 0) return;

        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const width = root.clientWidth;
        const height = root.clientHeight;
        const scrollLeft = read(model, "ScrollLeft", 0);
        const scrollTop = read(model, "ScrollTop", 0);
        const freezeRows = read(model, "FreezeRowCount", 0);
        const freezeCols = read(model, "FreezeColumnCount", 0);

        for (const cell of cells) {
            const validation = read(cell, "Validation", null);
            if (!validation) continue;
            if (read(validation, "Type", "") !== "List") continue;
            if (!read(validation, "ShowDropDown", true)) continue;

            const row = read(cell, "Row", 0);
            const col = read(cell, "Col", 0);
            const frozenCol = col < freezeCols;
            const frozenRow = row < freezeRows;
            const x = rowHeaderWidth + read(cell, "Left", 0) - (frozenCol ? 0 : scrollLeft);
            const y = columnHeaderHeight + read(cell, "Top", 0) - (frozenRow ? 0 : scrollTop);
            const w = read(cell, "Width", 0);
            const h = read(cell, "Height", 0);
            if (w <= 8 || h <= 4) continue;
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height) continue;

            const btn = validationDropButtonRect(x, y, w, h, getDensityScales(root).handleScale);
            if (btn.w <= 0 || btn.x < rowHeaderWidth || btn.y < columnHeaderHeight) continue;
            s.validationDropButtons.push({ row, col, x: btn.x, y: btn.y, w: btn.w, h: btn.h });

            ctx.save();
            ctx.fillStyle = palette.elevated;
            ctx.strokeStyle = palette.border;
            ctx.lineWidth = 1;
            ctx.fillRect(btn.x + 0.5, btn.y + 0.5, btn.w - 1, btn.h - 1);
            ctx.strokeRect(btn.x + 0.5, btn.y + 0.5, btn.w - 1, btn.h - 1);

            // Down-arrow glyph
            ctx.fillStyle = palette.muted;
            const cx = btn.x + btn.w / 2;
            const cy = btn.y + btn.h / 2;
            ctx.beginPath();
            ctx.moveTo(cx - 3, cy - 1.5);
            ctx.lineTo(cx + 3, cy - 1.5);
            ctx.lineTo(cx, cy + 2);
            ctx.closePath();
            ctx.fill();
            ctx.restore();
        }
    }

    function hitValidationDropButton(root, ev) {
        const s = getState(root);
        const buttons = s?.validationDropButtons;
        if (!buttons || buttons.length === 0) return null;
        const rect = root.getBoundingClientRect();
        const lx = ev.clientX - rect.left;
        const ly = ev.clientY - rect.top;
        for (const b of buttons) {
            if (lx >= b.x && lx <= b.x + b.w && ly >= b.y && ly <= b.y + b.h) return b;
        }
        return null;
    }

    function drawValidationCircles(ctx, root, model, palette) {
        const s = getState(root);
        if (!s) return;
        const refs = s.validationCircleRefs;
        if (!refs || refs.length === 0) return;

        const layout = getLayoutState(root);
        if (!layout) return;

        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const scrollLeft = read(model, "ScrollLeft", 0);
        const scrollTop = read(model, "ScrollTop", 0);
        const freezeRows = read(model, "FreezeRowCount", 0);
        const freezeCols = read(model, "FreezeColumnCount", 0);
        const canvasWidth = root.clientWidth;
        const canvasHeight = root.clientHeight;

        ctx.save();
        ctx.strokeStyle = palette.danger;
        ctx.lineWidth = 2;

        for (const ref of refs) {
            const parsed = parseCellRef(ref);
            if (!parsed) continue;
            const { row, col } = parsed;
            const frozen = row < freezeRows;
            const frozenCol = col < freezeCols;
            const rowFrame = createRowFrame(layout, row, scrollTop, frozen);
            const colFrame = createColumnFrame(layout, col, scrollLeft, frozenCol);
            const x = colFrame.x;
            const y = rowFrame.y;
            const w = colFrame.width;
            const h = rowFrame.height;
            if (w <= 0 || h <= 0) continue;
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > canvasWidth || y > canvasHeight) continue;

            const cx = x + w / 2;
            const cy = y + h / 2;
            const rx = w / 2 + 2;
            const ry = h / 2 + 2;
            ctx.beginPath();
            ctx.ellipse(cx, cy, rx, ry, 0, 0, Math.PI * 2);
            ctx.stroke();
        }

        ctx.restore();
    }

    function drawSelection(ctx, root, model, palette, width, height) {
        const metrics = getState(root)?.metrics;
        const rowHeaderWidth = read(model, "RowHeaderWidth", 40);
        const columnHeaderHeight = read(model, "ColumnHeaderHeight", 20);
        const scrollLeft = read(model, "ScrollLeft", 0);
        const scrollTop = read(model, "ScrollTop", 0);
        const freezeRows = read(model, "FreezeRowCount", 0);
        const freezeCols = read(model, "FreezeColumnCount", 0);
        const hover = getSheetHover(root);
        const formulaPoint = isFormulaPointMode(root);
        const autoFill = getAutoFillState(root);
        const layout = getVisibleLayout(root, model, width, height);
        const rows = layout.rows;
        const columns = layout.columns;
        // Comfortable draws a bigger fill handle: a 6px square is untouchable on a coarse pointer.
        const handleScale = getDensityScales(root).handleScale || 1;
        const handleSize = Math.round(6 * handleScale);
        const handleInset = Math.round(4 * handleScale);
        const active = parseCellRef(read(model, "ActiveCellRef", "A1"));
        const selection = read(model, "Selection", {});
        const startRow = read(selection, "StartRow", active.row);
        const startCol = read(selection, "StartCol", active.col);
        const endRow = read(selection, "EndRow", active.row);
        const endCol = read(selection, "EndCol", active.col);
        const minRow = Math.min(startRow, endRow);
        const maxRow = Math.max(startRow, endRow);
        const minCol = Math.min(startCol, endCol);
        const maxCol = Math.max(startCol, endCol);

        const drawCellOverlay = (rowFrame, colFrame, drawFill, drawActive, drawHandle) => {
            const row = read(rowFrame, "Index", 0);
            const col = read(colFrame, "Index", 0);
            const frozenCol = col < freezeCols;
            const frozenRow = row < freezeRows;
            const x = read(colFrame, "x", rowHeaderWidth + read(colFrame, "Left", 0) - (frozenCol ? 0 : scrollLeft));
            const y = read(rowFrame, "y", columnHeaderHeight + read(rowFrame, "Top", 0) - (frozenRow ? 0 : scrollTop));
            const w = read(colFrame, "Width", 0);
            const h = read(rowFrame, "Height", 0);
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height || w <= 0 || h <= 0) return;

            if (drawFill && !formulaPoint) {
                fillRectWithAlpha(ctx, palette.selectionFill, palette.selectionFillAlpha, x, y, w, h, metrics);
            }

            if (drawActive) {
                setContextStrokeStyle(ctx, palette.selectionBorder, metrics);
                setContextLineWidth(ctx, 2, metrics);
                ctx.strokeRect(Math.floor(x) + 1, Math.floor(y) + 1, Math.max(0, w - 2), Math.max(0, h - 2));
            }

            if (drawHandle) {
                setContextFillStyle(ctx, palette.selectionBorder, metrics);
                ctx.fillRect(x + w - handleInset, y + h - handleInset, handleSize, handleSize);
            }
        };

        for (const rowFrame of rows) {
            const row = read(rowFrame, "Index", -1);
            if (row < minRow || row > maxRow) continue;
            for (const colFrame of columns) {
                const col = read(colFrame, "Index", -1);
                if (col < minCol || col > maxCol) continue;
                drawCellOverlay(rowFrame, colFrame, true, row === active.row && col === active.col, row === endRow && col === endCol);
            }
        }

        if (hover) {
            const hoverRow = getFrameByIndex(rows, hover.row);
            const hoverCol = getFrameByIndex(columns, hover.col);
            const hoverInSelection = hover.row >= minRow && hover.row <= maxRow && hover.col >= minCol && hover.col <= maxCol;
            if (hoverRow && hoverCol && !hoverInSelection) {
                const frozenCol = hover.col < freezeCols;
                const frozenRow = hover.row < freezeRows;
                const x = read(hoverCol, "x", rowHeaderWidth + read(hoverCol, "Left", 0) - (frozenCol ? 0 : scrollLeft));
                const y = read(hoverRow, "y", columnHeaderHeight + read(hoverRow, "Top", 0) - (frozenRow ? 0 : scrollTop));
                const w = read(hoverCol, "Width", 0);
                const h = read(hoverRow, "Height", 0);
                if (x + w >= rowHeaderWidth && y + h >= columnHeaderHeight && x <= width && y <= height && w > 0 && h > 0) {
                    setContextFillStyle(ctx, palette.hoverFill, metrics);
                    ctx.fillRect(x, y, w, h);
                }
            }
        }

        if (autoFill?.active && autoFill.preview) {
            const preview = selectionBounds(autoFill.preview);
            const previewRect = getSelectionScreenRect(root, preview);
            if (previewRect) {
                ctx.save();
                setContextStrokeStyle(ctx, palette.primary, metrics);
                setContextLineWidth(ctx, 1.5, metrics);
                ctx.setLineDash([4, 3]);
                ctx.strokeRect(
                    Math.floor(previewRect.x) + 1,
                    Math.floor(previewRect.y) + 1,
                    Math.max(0, previewRect.width - 2),
                    Math.max(0, previewRect.height - 2));
                ctx.restore();
                resetContextState(ctx);
            }
        }

        const resize = getResizePreviewState(root);
        if (resize) {
            const rect = root.getBoundingClientRect();
            const minSize = resize.kind === "column" ? 16 : 8;
            const label = `${resize.kind === "column" ? "W" : "H"} ${Math.max(minSize, Math.round(resize.currentSize || resize.size))}`;
            if (resize.kind === "column") {
                const x = Math.max(rowHeaderWidth, Math.min(width - 1, resize.previewClient - rect.left));
                ctx.save();
                setContextStrokeStyle(ctx, palette.primary, metrics);
                setContextLineWidth(ctx, 2, metrics);
                ctx.setLineDash([5, 3]);
                ctx.beginPath();
                ctx.moveTo(Math.floor(x) + 0.5, 0);
                ctx.lineTo(Math.floor(x) + 0.5, height);
                ctx.stroke();
                ctx.restore();
                resetContextState(ctx);

                setContextFont(ctx, "600 11px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace", metrics);
                setContextTextAlign(ctx, "center", metrics);
                setContextTextBaseline(ctx, "middle", metrics);
                const labelWidth = Math.max(48, label.length * 7 + 12);
                const labelX = Math.max(rowHeaderWidth + labelWidth / 2, Math.min(width - labelWidth / 2 - 4, x));
                setContextFillStyle(ctx, palette.primary, metrics);
                ctx.fillRect(Math.round(labelX - labelWidth / 2), 4, labelWidth, 20);
                setContextFillStyle(ctx, palette.surface, metrics);
                ctx.fillText(label, labelX, 14);
            } else {
                const y = Math.max(columnHeaderHeight, Math.min(height - 1, resize.previewClient - rect.top));
                ctx.save();
                setContextStrokeStyle(ctx, palette.primary, metrics);
                setContextLineWidth(ctx, 2, metrics);
                ctx.setLineDash([5, 3]);
                ctx.beginPath();
                ctx.moveTo(0, Math.floor(y) + 0.5);
                ctx.lineTo(width, Math.floor(y) + 0.5);
                ctx.stroke();
                ctx.restore();
                resetContextState(ctx);

                setContextFont(ctx, "600 11px ui-monospace, SFMono-Regular, Menlo, Consolas, monospace", metrics);
                setContextTextAlign(ctx, "center", metrics);
                setContextTextBaseline(ctx, "middle", metrics);
                const labelWidth = Math.max(48, label.length * 7 + 12);
                const labelY = Math.max(columnHeaderHeight + 12, Math.min(height - 12, y));
                setContextFillStyle(ctx, palette.primary, metrics);
                ctx.fillRect(4, Math.round(labelY - 10), labelWidth, 20);
                setContextFillStyle(ctx, palette.surface, metrics);
                ctx.fillText(label, 4 + labelWidth / 2, labelY);
            }
        }

        const searchHighlights = getState(root)?.searchHighlights;
        if (searchHighlights && searchHighlights.set && searchHighlights.set.size > 0) {
            for (const rowFrame of rows) {
                const row = read(rowFrame, "Index", -1);
                for (const colFrame of columns) {
                    const col = read(colFrame, "Index", -1);
                    const key = `${row}|${col}`;
                    if (!searchHighlights.set.has(key)) continue;
                    const frozenCol = col < freezeCols;
                    const frozenRow = row < freezeRows;
                    const x = read(colFrame, "x", rowHeaderWidth + read(colFrame, "Left", 0) - (frozenCol ? 0 : scrollLeft));
                    const y = read(rowFrame, "y", columnHeaderHeight + read(rowFrame, "Top", 0) - (frozenRow ? 0 : scrollTop));
                    const w = read(colFrame, "Width", 0);
                    const h = read(rowFrame, "Height", 0);
                    if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height || w <= 0 || h <= 0) continue;
                    const isActive = searchHighlights.active === key;
                    setContextFillStyle(ctx, isActive ? palette.searchActiveFill : palette.searchMatchFill, metrics);
                    ctx.fillRect(x, y, w, h);
                    if (isActive) {
                        setContextStrokeStyle(ctx, palette.searchActiveStroke, metrics);
                        setContextLineWidth(ctx, 2, metrics);
                        ctx.strokeRect(Math.floor(x) + 1, Math.floor(y) + 1, Math.max(0, w - 2), Math.max(0, h - 2));
                    }
                }
            }
            resetContextState(ctx);
        }

        const formulaReferenceCells = getFormulaReferenceCells(root, model);
        if (formulaReferenceCells.length === 0) return;

        for (const cell of formulaReferenceCells) {
            const row = read(cell, "Row", 0);
            const col = read(cell, "Col", 0);
            const rowFrame = getFrameByIndex(rows, row);
            const colFrame = getFrameByIndex(columns, col);
            if (!rowFrame || !colFrame) continue;
            const frozenCol = col < freezeCols;
            const frozenRow = row < freezeRows;
            const cellLeft = read(cell, "Left", read(colFrame, "Left", 0));
            const cellTop = read(cell, "Top", read(rowFrame, "Top", 0));
            const x = read(colFrame, "x", rowHeaderWidth + cellLeft - (frozenCol ? 0 : scrollLeft));
            const y = read(rowFrame, "y", columnHeaderHeight + cellTop - (frozenRow ? 0 : scrollTop));
            const w = read(cell, "Width", read(colFrame, "Width", 0));
            const h = read(cell, "Height", read(rowFrame, "Height", 0));
            if (x + w < rowHeaderWidth || y + h < columnHeaderHeight || x > width || y > height || w <= 0 || h <= 0) continue;

            const formulaColorIndex = Number(read(cell, "FormulaRefColorIndex", -1));
            if (formulaColorIndex >= 0) {
                const refColor = palette.formulaRefs[formulaColorIndex % palette.formulaRefs.length];
                const activeFormulaToken = !!read(cell, "ActiveFormulaToken", false);
                setContextFillStyle(ctx, refColor.fill, metrics);
                ctx.fillRect(x, y, w, h);
                setContextStrokeStyle(ctx, refColor.stroke, metrics);
                setContextLineWidth(ctx, activeFormulaToken ? 3 : 2, metrics);
                ctx.strokeRect(
                    Math.floor(x) + (activeFormulaToken ? 0.5 : 1),
                    Math.floor(y) + (activeFormulaToken ? 0.5 : 1),
                    Math.max(0, w - (activeFormulaToken ? 1 : 2)),
                    Math.max(0, h - (activeFormulaToken ? 1 : 2)));
            }
        }
    }

    function drawGridLines(ctx, root, model, palette, width, height, layout) {
        const metrics = getState(root)?.metrics;
        layout = layout || getVisibleLayout(root, model, width, height);
        const rowHeaderWidth = layout.rowHeaderWidth;
        const columnHeaderHeight = layout.columnHeaderHeight;

        setContextStrokeStyle(ctx, palette.gridline, metrics);
        setContextLineWidth(ctx, 1, metrics);
        ctx.beginPath();
        const verticalLines = new Set();
        const horizontalLines = new Set();

        const columns = layout?.columns || [];
        const rows = layout?.rows || [];

        for (const col of columns) {
            const x = col.x;
            const w = col.width;
            const left = Math.floor(Math.max(rowHeaderWidth, x)) + 0.5;
            const right = Math.floor(x + w) + 0.5;
            if (left >= rowHeaderWidth && left <= width) verticalLines.add(left);
            if (right >= rowHeaderWidth && right <= width) verticalLines.add(right);
        }

        for (const row of rows) {
            const y = row.y;
            const h = row.height;
            const top = Math.floor(Math.max(columnHeaderHeight, y)) + 0.5;
            const bottom = Math.floor(y + h) + 0.5;
            if (top >= columnHeaderHeight && top <= height) horizontalLines.add(top);
            if (bottom >= columnHeaderHeight && bottom <= height) horizontalLines.add(bottom);
        }

        for (const x of verticalLines) {
            ctx.moveTo(x, columnHeaderHeight);
            ctx.lineTo(x, height);
        }

        for (const y of horizontalLines) {
            ctx.moveTo(rowHeaderWidth, y);
            ctx.lineTo(width, y);
        }

        ctx.stroke();
    }

    function hasBorders(style) {
        return hasVisibleBorder(read(style, "BorderTop", null))
            || hasVisibleBorder(read(style, "BorderRight", null))
            || hasVisibleBorder(read(style, "BorderBottom", null))
            || hasVisibleBorder(read(style, "BorderLeft", null));
    }

    function hasVisibleBorder(border) {
        return !!border && read(border, "Style", "none") !== "none";
    }

    function isSimpleTextCell(cell, style, paint, palette, imageUrl) {
        if (imageUrl) return false;
        const backgroundColor = read(style, "BackgroundColor", null);
        const foreColor = read(style, "ForeColor", null);
        if (backgroundColor && backgroundColor !== palette.surface) return false;
        if (foreColor && foreColor !== palette.text) return false;
        if (paint.horizontalAlign !== "left" || paint.verticalBaseline !== "bottom") return false;
        if (paint.underline || paint.doubleUnderline || paint.strikeThrough || paint.hyperlink) return false;
        if (hasBorders(style)) return false;
        if (read(style, "Bold", false) || read(style, "Italic", false)) return false;
        const fontFamily = read(style, "FontFamily", null);
        return !fontFamily || fontFamily === defaultCellFontFamily;
    }

    function textBoundsForAlign(align, textX, textWidth) {
        if (align === "right" || align === "end") return { start: textX - textWidth, end: textX };
        if (align === "center") return { start: textX - textWidth / 2, end: textX + textWidth / 2 };
        return { start: textX, end: textX + textWidth };
    }

    function finishCellContentMetrics(metrics, started) {
        if (!metrics) return;
        const contentMs = performance.now() - started;
        metrics.lastDrawCellContentMs += contentMs;
        metrics.drawCellContentTotalMs += contentMs;
    }

    function canUseCellSnapshot(snapshot, key, x, y, w, h) {
        if (!snapshot || snapshot.key !== key) return false;
        return Math.abs(snapshot.x - x) <= 0.5
            && Math.abs(snapshot.y - y) <= 0.5
            && Math.abs(snapshot.w - w) <= 0.5
            && Math.abs(snapshot.h - h) <= 0.5;
    }

    function renderCellSnapshot(root, cell, style, paint, palette, w, h, value, imageUrl) {
        if (w <= 0 || h <= 0 || imageUrl) return null;
        const dpr = window.devicePixelRatio || 1;
        const canvas = document.createElement("canvas");
        canvas.width = Math.max(1, Math.round(w * dpr));
        canvas.height = Math.max(1, Math.round(h * dpr));
        const ctx = canvas.getContext("2d");
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        resetContextState(ctx);
        const textDrawn = drawCellBody(ctx, root, cell, style, paint, palette, 0, 0, w, h, value, imageUrl);
        return { canvas, textDrawn, key: "", x: 0, y: 0, w, h };
    }

    function drawCellBody(ctx, root, cell, style, paint, palette, x, y, w, h, value, imageUrl) {
        const metrics = getState(root)?.metrics;
        const backgroundColor = paint.backgroundColor;
        if (backgroundColor && backgroundColor !== palette.surface) {
            setContextFillStyle(ctx, backgroundColor, metrics);
            ctx.fillRect(x, y, w, h);
        }

        if (!imageUrl && (value == null || value === "")) {
            if (metrics) metrics.skippedEmptyCellContentCount += 1;
            return false;
        }

        return drawCellContent(ctx, root, cell, style, paint, palette, x, y, w, h, value, imageUrl);
    }

    function drawCellContent(ctx, root, cell, style, paint, palette, x, y, w, h, displayValue, imageUrl) {
        const contentStarted = performance.now();
        const debugMetrics = getState(root)?.metrics;
        imageUrl = imageUrl ?? read(cell, "ImageUrl", null);
        if (imageUrl) {
            if (debugMetrics) debugMetrics.slowCellPathCount += 1;
            drawImage(ctx, root, imageUrl, x, y, w, h);
            finishCellContentMetrics(debugMetrics, contentStarted);
            return false;
        }

        const value = displayValue ?? getDisplayValue(root, cell);
        if (value == null || value === "") {
            if (debugMetrics) debugMetrics.skippedEmptyCellContentCount += 1;
            finishCellContentMetrics(debugMetrics, contentStarted);
            return false;
        }

        const font = getCanvasFont(root, style);
        const fontSize = (Number(read(style, "FontSize", 10)) || 10) * getRenderScale(root);
        setContextFont(ctx, font, debugMetrics);
        setContextFillStyle(ctx, paint.foreColor || palette.text, debugMetrics);
        setContextTextBaseline(ctx, paint.verticalBaseline, debugMetrics);
        setContextTextAlign(ctx, paint.horizontalAlign, debugMetrics);

        const padding = 4;
        const textX = textAnchorX(paint.horizontalAlignValue, x, w, padding);
        const textY = textAnchorY(paint.verticalAlignValue, y, h, padding);
        const textWidth = measureTextWidth(ctx, root, font, value);
        const bounds = textBoundsForAlign(paint.horizontalAlign, textX, textWidth);
        const needsClip = bounds.start < x + 1
            || bounds.end > x + w - 1
            || fontSize + 4 > h;
        const hasDecoration = paint.underline || paint.doubleUnderline || paint.strikeThrough || paint.hyperlink;
        const simple = isSimpleTextCell(cell, style, paint, palette, imageUrl);

        if (needsClip) {
            if (debugMetrics) {
                debugMetrics.slowCellPathCount += 1;
                debugMetrics.clippedTextCount += 1;
            }
            contextSave(ctx, debugMetrics);
            ctx.beginPath();
            ctx.rect(x + 1, y + 1, Math.max(0, w - 2), Math.max(0, h - 2));
            contextClip(ctx, debugMetrics);
        } else if (debugMetrics) {
            if (simple && !hasDecoration) debugMetrics.fastCellPathCount += 1;
            else debugMetrics.slowCellPathCount += 1;
            debugMetrics.unclippedTextCount += 1;
        }

        ctx.fillText(value, textX, textY);

        if (hasDecoration) {
            const lineY = paint.strikeThrough ? textY - fontSize * 0.25 : textY + 2;
            const startX = textXForDecoration(paint.horizontalAlign, textX, textWidth);
            setContextStrokeStyle(ctx, paint.foreColor || palette.text, debugMetrics);
            setContextLineWidth(ctx, 1, debugMetrics);
            ctx.beginPath();
            ctx.moveTo(startX, lineY);
            ctx.lineTo(startX + textWidth, lineY);
            if (paint.doubleUnderline && !paint.strikeThrough) {
                ctx.moveTo(startX, lineY + 2);
                ctx.lineTo(startX + textWidth, lineY + 2);
            }
            ctx.stroke();
        }

        if (needsClip) contextRestore(ctx, debugMetrics);
        finishCellContentMetrics(debugMetrics, contentStarted);
        return true;
    }

    function getDisplayValue(root, cell) {
        const rawValue = read(cell, "Value", "");
        if (rawValue == null || rawValue === "") return "";

        const style = read(cell, "Style", {});
        const key = [
            read(cell, "Ref", ""),
            read(style, "NumberFormat", ""),
            rawValue
        ].join("\n");
        const s = getState(root);
        const cache = s?.displayValueCache;
        if (!cache) return String(rawValue);

        const cached = cache.get(key);
        if (cached != null) return cached;

        const value = String(rawValue);
        cache.set(key, value);
        limitCache(cache, maxDisplayValueCacheSize);
        return value;
    }

    function getRenderZoom(root) {
        const s = getState(root);
        const zoom = Number(read(s?.model, "Zoom", 1)) || 1;
        return zoom > 0 ? zoom : 1;
    }

    /** Zoom × the density font scale: the cell text follows the grid density (SP1). */
    function getRenderScale(root) {
        const fontScale = getDensityScales(root).fontScale || 1;
        return getRenderZoom(root) * fontScale;
    }

    function getCanvasFont(root, style) {
        const scale = getRenderScale(root);
        const key = [
            read(style, "FontFamily", null) || "",
            read(style, "FontSize", 10),
            read(style, "Bold", false) ? 1 : 0,
            read(style, "Italic", false) ? 1 : 0,
            scale
        ].join("|");
        const s = getState(root);
        const cache = s?.fontStringCache;
        if (cache) {
            const cached = cache.get(key);
            if (cached) return cached;
        }

        const fontSize = (Number(read(style, "FontSize", 10)) || 10) * scale;
        const fontFamily = read(style, "FontFamily", null) || defaultCellFontFamily;
        const weight = read(style, "Bold", false) ? "700" : "400";
        const italic = read(style, "Italic", false) ? "italic " : "";
        const font = `${italic}${weight} ${fontSize}pt ${fontFamily}`;
        if (cache) {
            cache.set(key, font);
            limitCache(cache, maxStyleCacheSize);
        }
        return font;
    }

    function getCellPaint(root, cell, style, palette) {
        const key = [
            "cell",
            read(style, "BackgroundColor", null) || "",
            read(style, "ForeColor", null) || palette.text,
            read(style, "HorizontalAlign", "left"),
            read(style, "VerticalAlign", "bottom"),
            read(style, "Underline", false) ? 1 : 0,
            read(style, "DoubleUnderline", false) ? 1 : 0,
            read(style, "StrikeThrough", false) ? 1 : 0,
            read(cell, "Hyperlink", null) ? 1 : 0
        ].join("|");
        const s = getState(root);
        const cache = s?.paintStyleCache;
        if (cache) {
            const cached = cache.get(key);
            if (cached) return cached;
        }

        const horizontalAlignValue = read(style, "HorizontalAlign", "left");
        const verticalAlignValue = read(style, "VerticalAlign", "bottom");
        const hasHyperlink = !!read(cell, "Hyperlink", null);
        const paint = {
            backgroundColor: read(style, "BackgroundColor", null),
            foreColor: hasHyperlink ? palette.hyperlink : (read(style, "ForeColor", null) || palette.text),
            horizontalAlignValue,
            verticalAlignValue,
            horizontalAlign: horizontalAlign(horizontalAlignValue),
            verticalBaseline: verticalBaseline(verticalAlignValue),
            underline: !!read(style, "Underline", false) || hasHyperlink,
            doubleUnderline: !!read(style, "DoubleUnderline", false),
            strikeThrough: !!read(style, "StrikeThrough", false),
            hyperlink: hasHyperlink
        };

        if (cache) {
            cache.set(key, paint);
            limitCache(cache, maxStyleCacheSize);
        }
        return paint;
    }

    function measureTextWidth(ctx, root, font, value) {
        const s = getState(root);
        const cache = s?.textMetricsCache;
        if (!cache) return ctx.measureText(value).width;

        const key = font + "\n" + value;
        const cached = cache.get(key);
        if (cached != null) return cached;

        const width = ctx.measureText(value).width;
        cache.set(key, width);
        limitCache(cache, maxTextMeasureCacheSize);
        return width;
    }

    function drawImage(ctx, root, imageUrl, x, y, w, h) {
        let image = imageCache.get(imageUrl);
        if (!image) {
            image = new Image();
            image.onload = () => {
                const s = getState(root);
                if (s?.model) requestCanvasRedraw(root, "image", "content");
            };
            image.src = imageUrl;
            imageCache.set(imageUrl, image);
        }

        if (image.complete && image.naturalWidth > 0) {
            const scale = Math.min(w / image.naturalWidth, h / image.naturalHeight);
            const dw = image.naturalWidth * scale;
            const dh = image.naturalHeight * scale;
            ctx.drawImage(image, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh);
        }
    }

    function drawBorders(ctx, root, style, x, y, w, h, palette) {
        drawBorder(ctx, root, read(style, "BorderTop", null), x, y, x + w, y, palette);
        drawBorder(ctx, root, read(style, "BorderRight", null), x + w, y, x + w, y + h, palette);
        drawBorder(ctx, root, read(style, "BorderBottom", null), x, y + h, x + w, y + h, palette);
        drawBorder(ctx, root, read(style, "BorderLeft", null), x, y, x, y + h, palette);
    }

    function drawBorder(ctx, root, border, x1, y1, x2, y2, palette) {
        const paint = getBorderPaint(root, border, palette);
        if (!paint) return;
        const metrics = getState(root)?.metrics;
        setContextStrokeStyle(ctx, paint.color, metrics);
        setContextLineWidth(ctx, paint.width, metrics);
        ctx.setLineDash(paint.dash);
        ctx.beginPath();
        if (paint.double) {
            const horizontal = Math.abs(y1 - y2) < 0.5;
            const offset = 1.5;
            if (horizontal) {
                ctx.moveTo(x1, y1 - offset);
                ctx.lineTo(x2, y2 - offset);
                ctx.moveTo(x1, y1 + offset);
                ctx.lineTo(x2, y2 + offset);
            } else {
                ctx.moveTo(x1 - offset, y1);
                ctx.lineTo(x2 - offset, y2);
                ctx.moveTo(x1 + offset, y1);
                ctx.lineTo(x2 + offset, y2);
            }
        } else {
            ctx.moveTo(x1, y1);
            ctx.lineTo(x2, y2);
        }
        ctx.stroke();
        ctx.setLineDash([]);
    }

    function getBorderPaint(root, border, palette) {
        const style = read(border, "Style", "none");
        if (!border || style === "none") return null;

        const key = [
            "border",
            style,
            read(border, "Color", null) || palette.border
        ].join("|");
        const s = getState(root);
        const cache = s?.paintStyleCache;
        if (cache) {
            const cached = cache.get(key);
            if (cached) return cached;
        }

        const paint = {
            color: read(border, "Color", null) || palette.border,
            width: style === "medium" ? 2 : style === "thick" ? 3 : 1,
            dash: style === "dashed" ? [4, 3] : style === "dotted" ? [1, 2] : [],
            double: style === "double"
        };
        if (cache) {
            cache.set(key, paint);
            limitCache(cache, maxStyleCacheSize);
        }
        return paint;
    }

    function horizontalAlign(value) {
        if (value === "right") return "right";
        if (value === "center") return "center";
        return "left";
    }

    function textAnchorX(value, x, w, padding) {
        if (value === "right") return x + w - padding;
        if (value === "center") return x + w / 2;
        return x + padding;
    }

    function verticalBaseline(value) {
        if (value === "top") return "top";
        if (value === "middle") return "middle";
        return "bottom";
    }

    function textAnchorY(value, y, h, padding) {
        if (value === "top") return y + padding;
        if (value === "middle") return y + h / 2;
        return y + h - padding;
    }

    function textXForDecoration(align, x, width) {
        if (align === "right") return x - width;
        if (align === "center") return x - width / 2;
        return x;
    }

    /** Whether the browser reports a coarse pointer; a media query cannot be answered server-side. */
    window.tmSpreadsheetCanvas.isCoarsePointer = function () {
        return isCoarsePointer();
    };

    /**
     * The centre of a cell in the ROOT ELEMENT's coordinate space (what a Playwright
     * `Locator.ClickAsync(Position)` needs), derived from the grid's own painted geometry: the header
     * gutters, the logical scroll and the frozen panes all come from the frame, so a test never has to
     * hardcode 40/20 and can never drift from the canvas after a density change. Returns {-1,-1} for a
     * cell the grid does not know.
     */
    window.tmSpreadsheetCanvas.getCellCenter = function (root, row, col) {
        const s = getState(root);
        const model = s?.model;
        if (!model || !Number.isFinite(Number(row)) || !Number.isFinite(Number(col))) {
            return { x: -1, y: -1 };
        }
        const rowIndex = Number(row);
        const colIndex = Number(col);
        const rect = getCellScreenRect(root, rowIndex, colIndex);
        if (rect) {
            return {
                x: Math.round(rect.x + rect.width / 2),
                y: Math.round(rect.y + rect.height / 2)
            };
        }

        // Outside the painted window the frame's own row/column frames still give the geometry
        // (screenX/screenY apply the gutter, the frozen offset and the scroll).
        const rowFrame = (read(model, "Rows", []) || []).find(entry => read(entry, "Index", -1) === rowIndex);
        const colFrame = (read(model, "Columns", []) || []).find(entry => read(entry, "Index", -1) === colIndex);
        if (!rowFrame || !colFrame) return { x: -1, y: -1 };
        return {
            x: Math.round(screenX(model, colFrame) + read(colFrame, "Width", 0) / 2),
            y: Math.round(screenY(model, rowFrame) + read(rowFrame, "Height", 0) / 2)
        };
    };

    /**
     * The gutters the grid is actually laid out with (the component scaled them per density). E2E uses
     * it to assert the painted geometry instead of assuming 40/20.
     * <para>
     * Returns NULL when the root has no engine state/model: a caller must fail loudly instead of
     * silently measuring a 40/20 grid that the canvas never painted (getCellCenter returns {-1,-1} for
     * the same reason).
     * </para>
     */
    function resolveGridGutters(root) {
        const model = getState(root)?.model;
        if (!model) return null;
        return {
            rowHeaderWidth: read(model, "RowHeaderWidth", 40),
            columnHeaderHeight: read(model, "ColumnHeaderHeight", 20),
            density: String(read(model, "DensityResolved", "Compact")),
            gutterScale: getDensityScales(root).gutterScale
        };
    }

    window.tmSpreadsheetCanvas.getGridGutters = resolveGridGutters;

    /**
     * Whether the document's focus currently sits on a control OUTSIDE this grid. The grid claims the
     * focus after a canvas activation, but a render can land AFTER the user has already moved on (a
     * command-log flush, the viewport sync that the grid's own focusout schedules). Yanking the focus
     * back then blurs that control — the formula bar's name box resets its typed ref on blur and the
     * Enter that should navigate is swallowed by the grid's key handling instead. `body`/`html` (nothing
     * focused) count as "not outside", so the historical focusing behaviour is unchanged.
     */
    window.tmSpreadsheetCanvas.isFocusOutside = function (root) {
        if (!root) return false;
        let active = null;
        try { active = document.activeElement; } catch (e) { return false; }
        if (!active || active === document.body || active === document.documentElement) return false;
        try {
            return !(active === root || root.contains?.(active) === true);
        } catch (e) {
            return false;
        }
    };

    /**
     * Node-test seam for the PURE helpers (npm run test:spreadsheet loads this file in a `vm` sandbox):
     * the palette the tokens produce, the density resolution and the header highlight ranges. The
     * interaction surface stays the public functions above and is exercised by the E2E lane.
     */
    window.tmSpreadsheetCanvas._internals = {
        buildPalette,
        resolveDensity,
        resolveDensityFromModel,
        resolveHeaderHighlight,
        resolveHeaderStrips,
        withAlpha,
        isHighlightedIndex,
        isEditableKeyTarget,
        isSpreadsheetOverlayTarget,
        // Phase 7: the pure touch gesture recognizer (tap / double tap / drag origin / long press /
        // pinch zoom / hit areas). The sessions themselves are the shared core pointer helpers.
        resolvePointerKind,
        resolveTouchTap,
        resolveTouchDragAction,
        resolveLongPressAction,
        resolvePinchZoom,
        resolveCellInputMode,
        resolveAutoFillHitSize,
        resolveTouchHandleRects,
        clipTouchHandleRect,
        isCanvasShortcutCommand,
        resolveFilterButtonHitRect,
        // Phase 7b: the density gutter contract, the header font scale, the colour parser behind the
        // OPAQUE dark active header and the live pointer-kind decision (only Auto follows the media
        // query; an explicitly pinned density ignores it).
        parseColor,
        formatColor,
        resolveColorToken,
        resolveHeaderFont,
        resolveContentPoint,
        resolvePointerKindDensityDecision,
        attachPointerKindListener,
        // The engine getters are node-tested too: a root without a model must answer null/{ -1, -1 }
        // instead of silently measuring a 40/20 grid the canvas never painted.
        resolveGridGutters
    };
})();
