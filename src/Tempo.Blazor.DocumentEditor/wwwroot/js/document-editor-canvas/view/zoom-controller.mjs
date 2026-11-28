const MIN_ZOOM_SCALE = 0.25;
const MAX_ZOOM_SCALE = 4;
const DEFAULT_ZOOM_SCALE = 1;
const DEFAULT_STEP = 0.1;

export const ZOOM_PRESETS = Object.freeze({
    CUSTOM: 'custom',
    FIT_PAGE: 'fitPage',
    FIT_WIDTH: 'fitWidth',
    MULTIPLE_PAGES: 'multiplePages',
});

// Phase 11: the engine's own automatic fit of the page to the width of the document column.
//   off     - the zoom is the reader's / the host's: nothing is fitted for them (the default of a bare engine).
//   narrow  - fit ONLY when the page (plus its gutters) is wider than the column; never enlarges, 100 percent otherwise.
//   always  - the page always follows the column width (the "Fit width" preset, enlarging included).
// An explicit zoom of the reader (setZoom, zoomIn/Out, ctrl+wheel, pinch, fit page, multiple pages) switches it off until
// `fitWidth` / `resetZoom` / `setAutoFit` bring it back.
export const AUTO_FIT_MODES = Object.freeze({
    OFF: 'off',
    NARROW: 'narrow',
    ALWAYS: 'always',
});

// A page that overflows the column by no more than this many pixels is left alone; a fitted page lets go only when it would hold with this much spare.
export const AUTO_FIT_HYSTERESIS_PX = 2;

export function normalizeAutoFitMode(value) {
    const normalized = String(value ?? '').trim().toLowerCase();
    if (normalized === AUTO_FIT_MODES.NARROW) {
        return AUTO_FIT_MODES.NARROW;
    }

    if (normalized === AUTO_FIT_MODES.ALWAYS) {
        return AUTO_FIT_MODES.ALWAYS;
    }

    return AUTO_FIT_MODES.OFF;
}

export function normalizeZoomState(value = {}) {
    const preset = normalizeZoomPreset(value.preset || value.zoomPreset || ZOOM_PRESETS.CUSTOM);
    const scale = clampZoomScale(value.scale ?? percentToScale(value.percent ?? value.zoomPercent ?? 100));
    return {
        preset,
        scale,
        percent: scaleToPercent(scale),
        autoFit: normalizeAutoFitMode(value.autoFit ?? value.zoomAutoFit),
    };
}

/**
 * Pure auto-fit resolution: what scale / preset the page should have for the given column.
 * `applies` is false while the mode is off (the caller keeps the reader's zoom).
 * options.hysteresis (px, default 2) and options.scrollbarAllowance (px, default 0, widens only the margin at which a FITTED page lets
 * go - a vertical scrollbar that disappears with the fit must not flip the page straight back to 100 percent).
 */
export function resolveAutoFitScale(state = {}, metrics = {}, options = {}) {
    const current = normalizeZoomState(state);
    if (current.autoFit === AUTO_FIT_MODES.OFF) {
        return { applies: false, scale: current.scale, preset: current.preset, autoFit: current.autoFit };
    }

    const normalized = normalizeZoomMetrics(metrics);
    const fitScale = computePresetScale(ZOOM_PRESETS.FIT_WIDTH, normalized);
    if (current.autoFit === AUTO_FIT_MODES.ALWAYS) {
        return { applies: true, scale: fitScale, preset: ZOOM_PRESETS.FIT_WIDTH, autoFit: current.autoFit };
    }

    const hysteresis = Math.max(0, Number(options.hysteresis ?? AUTO_FIT_HYSTERESIS_PX) || 0);
    const allowance = Math.max(0, Number(options.scrollbarAllowance ?? 0) || 0);
    const overflow = normalized.pageWidth + normalized.paddingInline - normalized.viewportWidth;
    const wasFitted = current.preset === ZOOM_PRESETS.FIT_WIDTH;
    const fit = wasFitted ? overflow >= -(hysteresis + allowance) : overflow > hysteresis;
    if (fit) {
        return {
            applies: true,
            scale: snapZoomScale(Math.min(DEFAULT_ZOOM_SCALE, fitScale)),
            preset: ZOOM_PRESETS.FIT_WIDTH,
            autoFit: current.autoFit,
        };
    }

    return { applies: true, scale: DEFAULT_ZOOM_SCALE, preset: ZOOM_PRESETS.CUSTOM, autoFit: current.autoFit };
}

export function applyZoomCommand(currentState = {}, commandId, argument = null, metrics = {}) {
    const normalized = normalizeZoomCommand(commandId);
    const current = normalizeZoomState(currentState);
    const viewportMetrics = normalizeZoomMetrics(metrics);
    let next;

    // The reader's own zoom wins over the automatic fit until the fit is asked for again (fitWidth / resetZoom / setAutoFit).
    const readerLock = AUTO_FIT_MODES.OFF;

    switch (normalized) {
        case 'setzoom':
        case 'customzoom':
            next = {
                preset: ZOOM_PRESETS.CUSTOM,
                scale: clampZoomScale(argumentScale(argument)),
                autoFit: readerLock,
            };
            break;
        case 'zoomin':
            next = {
                preset: ZOOM_PRESETS.CUSTOM,
                scale: clampZoomScale(current.scale + stepFromArgument(argument)),
                autoFit: readerLock,
            };
            break;
        case 'zoomout':
            next = {
                preset: ZOOM_PRESETS.CUSTOM,
                scale: clampZoomScale(current.scale - stepFromArgument(argument)),
                autoFit: readerLock,
            };
            break;
        case 'fitpage':
            next = {
                preset: ZOOM_PRESETS.FIT_PAGE,
                scale: computePresetScale(ZOOM_PRESETS.FIT_PAGE, viewportMetrics),
                autoFit: readerLock,
            };
            break;
        case 'fitwidth':
        case 'zoompagewidth':
            // An explicit "fit width" is the reader asking for the fit: the page always follows the column from then on (enlarging included).
            // The narrow-only mode is a host configuration (`PageFitMode`) that `resetZoom` / `setAutoFit` bring back, never an argument of this command.
            next = {
                preset: ZOOM_PRESETS.FIT_WIDTH,
                scale: computePresetScale(ZOOM_PRESETS.FIT_WIDTH, viewportMetrics),
                autoFit: AUTO_FIT_MODES.ALWAYS,
            };
            break;
        case 'multiplepages':
        case 'twopages':
            next = {
                preset: ZOOM_PRESETS.MULTIPLE_PAGES,
                scale: computePresetScale(ZOOM_PRESETS.MULTIPLE_PAGES, viewportMetrics),
                autoFit: readerLock,
            };
            break;
        case 'ctrlwheelzoom':
        case 'pinchzoom':
            next = {
                preset: ZOOM_PRESETS.CUSTOM,
                scale: clampZoomScale(current.scale * zoomFactorFromGesture(argument, normalized)),
                autoFit: readerLock,
            };
            break;
        case 'applyautofit':
            // The observer re-resolves the fit for the current column; a mode that is off keeps the reader's zoom.
            next = autoFitNext(current, current.autoFit, viewportMetrics, argument);
            break;
        case 'setautofit':
            next = setAutoFitNext(current, normalizeAutoFitMode(argument?.mode ?? argument?.autoFit ?? argument), viewportMetrics, argument);
            break;
        case 'resetzoom':
            // "Reset zoom": back to the configured automatic behaviour from a clean 100 percent (the fit hysteresis starts unfitted).
            // The configured mode is part of the command CONTEXT (`metrics.autoFitMode`, filled by the engine), never patched into the argument.
            next = autoFitNext(
                { preset: ZOOM_PRESETS.CUSTOM, scale: DEFAULT_ZOOM_SCALE, percent: 100, autoFit: AUTO_FIT_MODES.OFF },
                normalizeAutoFitMode(argument?.mode ?? argument?.autoFit ?? viewportMetrics.autoFitMode),
                viewportMetrics,
                argument);
            break;
        default:
            return {
                handled: false,
                changed: false,
                state: current,
            };
    }

    next.scale = snapZoomScale(next.scale);
    next.percent = scaleToPercent(next.scale);
    next.autoFit = normalizeAutoFitMode(next.autoFit);
    const changed = next.preset !== current.preset
        || Math.abs(next.scale - current.scale) > 0.0001
        || next.autoFit !== current.autoFit;
    return {
        handled: true,
        changed,
        viewChanged: changed,
        state: next,
        zoom: next,
    };
}

export function isZoomCommand(commandId) {
    return [
        'setzoom',
        'customzoom',
        'zoomin',
        'zoomout',
        'fitpage',
        'fitwidth',
        'zoompagewidth',
        'multiplepages',
        'twopages',
        'ctrlwheelzoom',
        'pinchzoom',
        'applyautofit',
        'setautofit',
        'resetzoom',
    ].includes(normalizeZoomCommand(commandId));
}

export function computePresetScale(preset, metrics = {}) {
    const normalized = normalizeZoomMetrics(metrics);
    const availableWidth = Math.max(1, normalized.viewportWidth - normalized.paddingInline);
    const availableHeight = Math.max(1, normalized.viewportHeight - normalized.paddingBlock);

    if (preset === ZOOM_PRESETS.FIT_WIDTH) {
        return snapZoomScale(clampZoomScale(availableWidth / normalized.pageWidth));
    }

    if (preset === ZOOM_PRESETS.FIT_PAGE) {
        return snapZoomScale(clampZoomScale(Math.min(
            availableWidth / normalized.pageWidth,
            availableHeight / normalized.pageHeight,
        )));
    }

    if (preset === ZOOM_PRESETS.MULTIPLE_PAGES) {
        const twoPageWidth = normalized.pageWidth * 2 + normalized.pageGap;
        return snapZoomScale(clampZoomScale(Math.min(
            availableWidth / twoPageWidth,
            availableHeight / normalized.pageHeight,
        )));
    }

    return DEFAULT_ZOOM_SCALE;
}

export function zoomedLength(value, zoomState = {}) {
    const scale = normalizeZoomState(zoomState).scale;
    return Math.max(0, (Number(value) || 0) * scale);
}

export function scaleToPercent(scale) {
    return Math.round(clampZoomScale(scale) * 100);
}

export function percentToScale(percent) {
    return clampZoomScale((Number(percent) || 100) / 100);
}

export function snapZoomScale(scale) {
    return Math.round(clampZoomScale(scale) * 1000) / 1000;
}

export function normalizeZoomPreset(value) {
    const normalized = String(value || '').replace(/[\s_-]/g, '').toLowerCase();
    if (normalized === 'fitpage' || normalized === 'page') {
        return ZOOM_PRESETS.FIT_PAGE;
    }

    if (normalized === 'fitwidth' || normalized === 'pagewidth' || normalized === 'width') {
        return ZOOM_PRESETS.FIT_WIDTH;
    }

    if (normalized === 'multiplepages' || normalized === 'twopages' || normalized === 'multipage') {
        return ZOOM_PRESETS.MULTIPLE_PAGES;
    }

    return ZOOM_PRESETS.CUSTOM;
}

export function normalizeZoomMetrics(metrics = {}) {
    const page = Array.isArray(metrics?.pages) && metrics.pages.length > 0 ? metrics.pages[0] : null;
    return {
        pageWidth: Math.max(1, Number(metrics.pageWidth ?? page?.width ?? 794) || 794),
        pageHeight: Math.max(1, Number(metrics.pageHeight ?? page?.height ?? 1123) || 1123),
        viewportWidth: Math.max(1, Number(metrics.viewportWidth ?? globalThis.innerWidth ?? 1280) || 1280),
        viewportHeight: Math.max(1, Number(metrics.viewportHeight ?? globalThis.innerHeight ?? 900) || 900),
        pageGap: Math.max(0, Number(metrics.pageGap ?? 24) || 0),
        paddingInline: Math.max(0, Number(metrics.paddingInline ?? 48) || 0),
        paddingBlock: Math.max(0, Number(metrics.paddingBlock ?? 48) || 0),
        // The mode the host configured (`PageFitMode`): the target of `resetZoom`. Not a measurement, a piece of context of the command.
        autoFitMode: normalizeAutoFitMode(metrics.autoFitMode),
    };
}

// The automatic fit of `mode` for `from` (the zoom the page has now). An off mode keeps `from`.
function autoFitNext(from, mode, metrics, argument) {
    const resolved = resolveAutoFitScale({ ...from, autoFit: mode }, metrics, autoFitOptions(argument));
    if (!resolved.applies) {
        return { preset: from.preset, scale: from.scale, autoFit: mode };
    }

    return { preset: resolved.preset, scale: resolved.scale, autoFit: mode };
}

// Changing the mode: a fit applied earlier gives way to 100 percent when the mode goes off, otherwise the new mode resolves at once.
function setAutoFitNext(current, mode, metrics, argument) {
    if (mode === AUTO_FIT_MODES.OFF) {
        const wasFitted = current.autoFit !== AUTO_FIT_MODES.OFF && current.preset === ZOOM_PRESETS.FIT_WIDTH;
        return wasFitted
            ? { preset: ZOOM_PRESETS.CUSTOM, scale: DEFAULT_ZOOM_SCALE, autoFit: mode }
            : { preset: current.preset, scale: current.scale, autoFit: mode };
    }

    return autoFitNext(current, mode, metrics, argument);
}

function autoFitOptions(argument) {
    return {
        hysteresis: argument?.hysteresis,
        scrollbarAllowance: argument?.scrollbarAllowance,
    };
}

function normalizeZoomCommand(commandId) {
    return String(commandId || '').replace(/[\s_-]/g, '').toLowerCase();
}

function argumentScale(argument) {
    if (typeof argument === 'number') {
        return percentToScale(argument);
    }

    return clampZoomScale(argument?.scale ?? percentToScale(argument?.percent ?? argument?.zoomPercent ?? 100));
}

function stepFromArgument(argument) {
    if (typeof argument === 'number') {
        return Math.max(0.01, Math.min(1, argument));
    }

    return Math.max(0.01, Math.min(1, Number(argument?.step || DEFAULT_STEP) || DEFAULT_STEP));
}

function zoomFactorFromGesture(argument, commandId) {
    const explicitScale = Number(argument?.scaleDelta ?? argument?.factor ?? 0);
    if (explicitScale > 0) {
        return Math.max(0.1, Math.min(10, explicitScale));
    }

    const delta = Number(argument?.deltaY ?? argument?.wheelDelta ?? 0) || 0;
    if (delta === 0 && commandId === 'pinchzoom') {
        return Math.max(0.1, Math.min(10, Number(argument?.pinchScale || 1) || 1));
    }

    return delta < 0 ? 1.1 : 0.9;
}

function clampZoomScale(scale) {
    return Math.max(MIN_ZOOM_SCALE, Math.min(MAX_ZOOM_SCALE, Number(scale) || DEFAULT_ZOOM_SCALE));
}
