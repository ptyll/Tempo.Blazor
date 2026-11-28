import { applyZoomCommand, isZoomCommand, normalizeZoomMetrics, normalizeZoomState, ZOOM_PRESETS } from './zoom-controller.mjs';

export const CANVAS_VIEW_MODES = Object.freeze({
    PRINT: 'print',
    READING: 'reading',
    WEB: 'web',
    OUTLINE: 'outline',
});

export function createCanvasViewState(initial = {}) {
    return {
        viewMode: normalizeCanvasViewMode(initial.viewMode || initial.mode || CANVAS_VIEW_MODES.PRINT),
        zoom: normalizeZoomState(initial.zoom || initial),
        scrollAnchor: normalizeScrollAnchor(initial.scrollAnchor),
        printPreview: {
            active: initial.printPreview?.active === true,
        },
    };
}

export function applyCanvasViewCommand(currentState = {}, commandId, argument = null, metrics = {}) {
    const normalized = normalizeViewCommand(commandId);
    const state = createCanvasViewState(currentState);

    if (isZoomCommand(normalized)) {
        const zoomResult = applyZoomCommand(state.zoom, normalized, argument, normalizeZoomMetrics(metrics));
        if (!zoomResult.handled) {
            return { handled: false, changed: false, state };
        }

        return {
            handled: true,
            changed: false,
            viewChanged: zoomResult.changed,
            state: {
                ...state,
                zoom: zoomResult.state,
                scrollAnchor: normalizeScrollAnchor(argument?.scrollAnchor || state.scrollAnchor),
            },
            operation: 'zoom',
            zoom: zoomResult.state,
        };
    }

    if (normalized === 'setviewmode' || normalized === 'viewmode') {
        return setViewMode(state, argument?.mode || argument?.viewMode || argument);
    }

    if (normalized === 'printlayout') {
        return setViewMode(state, CANVAS_VIEW_MODES.PRINT);
    }

    if (normalized === 'readingmode' || normalized === 'readmode') {
        return setViewMode(state, CANVAS_VIEW_MODES.READING);
    }

    if (normalized === 'weblayout' || normalized === 'webmode') {
        return setViewMode(state, CANVAS_VIEW_MODES.WEB);
    }

    if (normalized === 'outlineview' || normalized === 'outlinemode') {
        return setViewMode(state, CANVAS_VIEW_MODES.OUTLINE);
    }

    if (normalized === 'openprintpreview' || normalized === 'printpreview') {
        const next = {
            ...state,
            printPreview: { active: true },
        };
        return viewResult(state, next, 'printPreview');
    }

    if (normalized === 'closeprintpreview') {
        const next = {
            ...state,
            printPreview: { active: false },
        };
        return viewResult(state, next, 'printPreview');
    }

    if (normalized === 'printdocument' || normalized === 'print') {
        const next = {
            ...state,
            printPreview: { active: true, printRequested: true },
        };
        return {
            ...viewResult(state, next, 'print'),
            printRequested: true,
        };
    }

    return { handled: false, changed: false, state };
}

export function isCanvasViewModeCommand(commandId) {
    const normalized = normalizeViewCommand(commandId);
    return isZoomCommand(normalized) || [
        'setviewmode',
        'viewmode',
        'printlayout',
        'readingmode',
        'readmode',
        'weblayout',
        'webmode',
        'outlineview',
        'outlinemode',
        'openprintpreview',
        'printpreview',
        'closeprintpreview',
        'printdocument',
        'print',
    ].includes(normalized);
}

export function queryCanvasViewCommandState(state = {}) {
    const current = createCanvasViewState(state);
    const mode = current.viewMode;
    return {
        commands: {
            setViewMode: commandState(false, mode),
            printLayout: commandState(mode === CANVAS_VIEW_MODES.PRINT, CANVAS_VIEW_MODES.PRINT),
            readingMode: commandState(mode === CANVAS_VIEW_MODES.READING, CANVAS_VIEW_MODES.READING),
            webLayout: commandState(mode === CANVAS_VIEW_MODES.WEB, CANVAS_VIEW_MODES.WEB),
            outlineView: commandState(mode === CANVAS_VIEW_MODES.OUTLINE, CANVAS_VIEW_MODES.OUTLINE),
            setZoom: commandState(false, current.zoom.percent),
            fitPage: commandState(current.zoom.preset === ZOOM_PRESETS.FIT_PAGE, current.zoom.percent),
            fitWidth: commandState(current.zoom.preset === ZOOM_PRESETS.FIT_WIDTH, current.zoom.percent),
            multiplePages: commandState(current.zoom.preset === ZOOM_PRESETS.MULTIPLE_PAGES, current.zoom.percent),
            openPrintPreview: commandState(current.printPreview.active === true, current.printPreview.active === true),
            printDocument: commandState(false, current.printPreview.active === true),
        },
        view: {
            viewMode: mode,
            mode,
            toolbarHidden: mode === CANVAS_VIEW_MODES.READING,
            zoom: current.zoom,
            zoomPercent: current.zoom.percent,
            zoomPreset: current.zoom.preset,
            zoomAutoFit: current.zoom.autoFit,
            scrollAnchor: current.scrollAnchor,
            printPreview: {
                active: current.printPreview.active === true,
            },
        },
    };
}

export function normalizeCanvasViewMode(value) {
    const normalized = String(value || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'reading' || normalized === 'read') {
        return CANVAS_VIEW_MODES.READING;
    }

    if (normalized === 'web' || normalized === 'weblayout') {
        return CANVAS_VIEW_MODES.WEB;
    }

    if (normalized === 'outline' || normalized === 'outlineview') {
        return CANVAS_VIEW_MODES.OUTLINE;
    }

    return CANVAS_VIEW_MODES.PRINT;
}

export function viewPresentation(state = {}) {
    const current = createCanvasViewState(state);
    if (current.viewMode === CANVAS_VIEW_MODES.READING) {
        return {
            mode: current.viewMode,
            toolbarHidden: true,
            rootGap: 16,
            rootPadding: 16,
            pageShadow: '0 8px 20px rgba(15, 23, 42, 0.10)',
            pageBorder: '1px solid rgba(148, 163, 184, 0.32)',
        };
    }

    if (current.viewMode === CANVAS_VIEW_MODES.WEB) {
        return {
            mode: current.viewMode,
            toolbarHidden: false,
            rootGap: 12,
            rootPadding: 18,
            pageShadow: 'none',
            pageBorder: '1px solid rgba(148, 163, 184, 0.28)',
        };
    }

    if (current.viewMode === CANVAS_VIEW_MODES.OUTLINE) {
        return {
            mode: current.viewMode,
            toolbarHidden: false,
            rootGap: 18,
            rootPadding: 20,
            pageShadow: '0 10px 24px rgba(15, 23, 42, 0.12)',
            pageBorder: '1px dashed rgba(79, 70, 229, 0.42)',
        };
    }

    return {
        mode: current.viewMode,
        toolbarHidden: false,
        rootGap: 24,
        rootPadding: 24,
        // Tokens first (dark mode swaps them: a light page on a dark workspace is told apart by the 1px outline and the dark-aware shadow); the literal is only the
        // fallback of a host without the design tokens.
        pageShadow: 'var(--tm-shadow-lg, 0 14px 34px rgba(15, 23, 42, 0.18))',
        pageBorder: '1px solid var(--tm-border-color, rgba(148, 163, 184, 0.45))',
    };
}

function setViewMode(state, mode) {
    const next = {
        ...state,
        viewMode: normalizeCanvasViewMode(mode),
    };
    return viewResult(state, next, 'viewMode');
}

function viewResult(before, after, operation) {
    const changed = JSON.stringify(before) !== JSON.stringify(after);
    return {
        handled: true,
        changed: false,
        viewChanged: changed,
        state: after,
        operation,
    };
}

function normalizeViewCommand(commandId) {
    return String(commandId || '').replace(/[\s_-]/g, '').toLowerCase();
}

function normalizeScrollAnchor(value) {
    if (!value || typeof value !== 'object') {
        return null;
    }

    return {
        pageIndex: Number(value.pageIndex || 0) || 0,
        blockId: String(value.blockId || ''),
        offset: Number(value.offset || 0) || 0,
        viewportTop: Number(value.viewportTop || 0) || 0,
    };
}

function commandState(active, value) {
    return {
        disabled: false,
        active: active === true,
        mixed: false,
        value,
        state: active === true ? 'active' : 'inactive',
    };
}
