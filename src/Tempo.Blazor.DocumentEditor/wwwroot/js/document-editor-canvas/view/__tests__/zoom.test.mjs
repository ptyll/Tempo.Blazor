import test from 'node:test';
import assert from 'node:assert/strict';
import {
    applyZoomCommand,
    AUTO_FIT_MODES,
    computePresetScale,
    isZoomCommand,
    normalizeAutoFitMode,
    normalizeZoomState,
    percentToScale,
    resolveAutoFitScale,
    scaleToPercent,
    ZOOM_PRESETS,
} from '../zoom-controller.mjs';

test('zoom controller normalizes percent and clamps the production zoom range', () => {
    assert.deepEqual(normalizeZoomState({ percent: 125 }), {
        preset: ZOOM_PRESETS.CUSTOM,
        scale: 1.25,
        percent: 125,
        autoFit: AUTO_FIT_MODES.OFF,
    });
    assert.equal(percentToScale(10), 0.25);
    assert.equal(percentToScale(600), 4);
    assert.equal(scaleToPercent(1.375), 138);
});

test('zoom controller computes fit width, fit page and multiple pages from viewport metrics', () => {
    const metrics = {
        pageWidth: 800,
        pageHeight: 1000,
        viewportWidth: 1200,
        viewportHeight: 900,
        pageGap: 24,
        paddingInline: 48,
        paddingBlock: 48,
    };

    assert.equal(computePresetScale(ZOOM_PRESETS.FIT_WIDTH, metrics), 1.44);
    assert.equal(computePresetScale(ZOOM_PRESETS.FIT_PAGE, metrics), 0.852);
    assert.equal(computePresetScale(ZOOM_PRESETS.MULTIPLE_PAGES, metrics), 0.709);
});

test('zoom commands expose changed view state without mutating document content', () => {
    const metrics = {
        pageWidth: 794,
        pageHeight: 1123,
        viewportWidth: 1440,
        viewportHeight: 1000,
    };

    const fitWidth = applyZoomCommand(normalizeZoomState(), 'fitWidth', null, metrics);
    assert.equal(fitWidth.handled, true);
    assert.equal(fitWidth.changed, true);
    assert.equal(fitWidth.state.preset, ZOOM_PRESETS.FIT_WIDTH);
    assert.ok(fitWidth.state.percent > 100);

    const custom = applyZoomCommand(fitWidth.state, 'setZoom', { percent: 80 }, metrics);
    assert.equal(custom.state.preset, ZOOM_PRESETS.CUSTOM);
    assert.equal(custom.state.scale, 0.8);

    const wheel = applyZoomCommand(custom.state, 'ctrlWheelZoom', { deltaY: -100 }, metrics);
    assert.equal(wheel.state.preset, ZOOM_PRESETS.CUSTOM);
    assert.equal(wheel.state.percent, 88);
});

// ── Phase 11: engine auto-fit (narrow / always / off) ──────────────────────

const A4 = { pageWidth: 794, pageHeight: 1123, viewportHeight: 900, paddingInline: 48, paddingBlock: 48 };

test('autoFit normalizes to off / narrow / always and defaults to off', () => {
    assert.equal(normalizeAutoFitMode(undefined), 'off');
    assert.equal(normalizeAutoFitMode('NARROW'), 'narrow');
    assert.equal(normalizeAutoFitMode('always'), 'always');
    assert.equal(normalizeAutoFitMode('bogus'), 'off');
    assert.equal(normalizeZoomState({ autoFit: 'narrow' }).autoFit, 'narrow');
    assert.equal(normalizeZoomState({ zoomAutoFit: 'always' }).autoFit, 'always');
});

test('resolveAutoFitScale off leaves the reader zoom alone', () => {
    const result = resolveAutoFitScale({ autoFit: 'off', scale: 1.2, preset: 'custom' }, { ...A4, viewportWidth: 500 });
    assert.equal(result.applies, false);
    assert.equal(result.scale, 1.2);
    assert.equal(result.preset, ZOOM_PRESETS.CUSTOM);
});

test('resolveAutoFitScale narrow fits a page wider than the viewport and never enlarges', () => {
    const narrow = resolveAutoFitScale({ autoFit: 'narrow', scale: 1, preset: 'custom' }, { ...A4, viewportWidth: 650 });
    assert.equal(narrow.applies, true);
    assert.equal(narrow.preset, ZOOM_PRESETS.FIT_WIDTH);
    assert.equal(narrow.scale, computePresetScale(ZOOM_PRESETS.FIT_WIDTH, { ...A4, viewportWidth: 650 }));
    assert.ok(narrow.scale < 1);

    const wide = resolveAutoFitScale({ autoFit: 'narrow', scale: 0.76, preset: ZOOM_PRESETS.FIT_WIDTH }, { ...A4, viewportWidth: 1920 });
    assert.equal(wide.scale, 1, 'a viewport that holds the page shows it at 100 percent, never larger');
    assert.equal(wide.preset, ZOOM_PRESETS.CUSTOM);
});

test('resolveAutoFitScale always follows the width, including enlarging', () => {
    const result = resolveAutoFitScale({ autoFit: 'always', scale: 1, preset: 'custom' }, { ...A4, viewportWidth: 1200 });
    assert.equal(result.preset, ZOOM_PRESETS.FIT_WIDTH);
    assert.ok(result.scale > 1);
});

test('resolveAutoFitScale keeps a 0.25 floor on a very narrow container', () => {
    const result = resolveAutoFitScale({ autoFit: 'narrow', scale: 1, preset: 'custom' }, { ...A4, viewportWidth: 120 });
    assert.equal(result.scale, 0.25);
});

test('resolveAutoFitScale hysteresis of 2 px prevents oscillation around the threshold', () => {
    // page + padding = 842. Unfitted: needs to overflow by MORE than 2 px to fit.
    const unfitted = { autoFit: 'narrow', scale: 1, preset: 'custom' };
    assert.equal(resolveAutoFitScale(unfitted, { ...A4, viewportWidth: 841 }).preset, ZOOM_PRESETS.CUSTOM, '1 px short stays at 100 percent');
    assert.equal(resolveAutoFitScale(unfitted, { ...A4, viewportWidth: 840 }).preset, ZOOM_PRESETS.CUSTOM, 'exactly 2 px short stays');
    assert.equal(resolveAutoFitScale(unfitted, { ...A4, viewportWidth: 839 }).preset, ZOOM_PRESETS.FIT_WIDTH, '3 px short fits');

    // Fitted: gives way only when the page holds with 2 px to spare.
    const fitted = { autoFit: 'narrow', scale: 0.99, preset: ZOOM_PRESETS.FIT_WIDTH };
    assert.equal(resolveAutoFitScale(fitted, { ...A4, viewportWidth: 842 }).preset, ZOOM_PRESETS.FIT_WIDTH, 'exactly holding is not enough');
    assert.equal(resolveAutoFitScale(fitted, { ...A4, viewportWidth: 844 }).preset, ZOOM_PRESETS.FIT_WIDTH, '2 px spare stays fitted');
    assert.equal(resolveAutoFitScale(fitted, { ...A4, viewportWidth: 845 }).preset, ZOOM_PRESETS.CUSTOM, '3 px spare lets go');
});

test('resolveAutoFitScale scrollbar allowance widens only the fitted state release margin', () => {
    const fitted = { autoFit: 'narrow', scale: 0.99, preset: ZOOM_PRESETS.FIT_WIDTH };
    assert.equal(resolveAutoFitScale(fitted, { ...A4, viewportWidth: 850 }, { scrollbarAllowance: 16 }).preset, ZOOM_PRESETS.FIT_WIDTH);
    assert.equal(resolveAutoFitScale(fitted, { ...A4, viewportWidth: 861 }, { scrollbarAllowance: 16 }).preset, ZOOM_PRESETS.CUSTOM);
    const unfitted = { autoFit: 'narrow', scale: 1, preset: 'custom' };
    assert.equal(resolveAutoFitScale(unfitted, { ...A4, viewportWidth: 839 }, { scrollbarAllowance: 16 }).preset, ZOOM_PRESETS.FIT_WIDTH);
});

test('reader zoom commands switch autoFit off; fitWidth switches it on; setAutoFit and resetZoom restore a mode', () => {
    const metrics = { ...A4, viewportWidth: 650 };
    const start = normalizeZoomState({ autoFit: 'narrow', percent: 100 });
    for (const [command, argument] of [['setZoom', { percent: 120 }], ['zoomIn', null], ['zoomOut', null], ['ctrlWheelZoom', { deltaY: -100 }], ['pinchZoom', { scaleDelta: 1.2 }], ['fitPage', null]]) {
        const result = applyZoomCommand(start, command, argument, metrics);
        assert.equal(result.state.autoFit, 'off', `${command} is the reader's own zoom`);
    }

    const fit = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 150 }), 'fitWidth', null, metrics);
    assert.equal(fit.state.autoFit, 'always');
    assert.equal(fit.state.preset, ZOOM_PRESETS.FIT_WIDTH);

    const reset = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 150 }), 'resetZoom', { mode: 'narrow' }, metrics);
    assert.equal(reset.state.autoFit, 'narrow');
    assert.equal(reset.state.preset, ZOOM_PRESETS.FIT_WIDTH, 'a narrow viewport fits again');
    assert.ok(reset.state.scale < 1);

    const roomy = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 150 }), 'resetZoom', { mode: 'narrow' }, { ...A4, viewportWidth: 1600 });
    assert.equal(roomy.state.scale, 1);
    assert.equal(roomy.state.preset, ZOOM_PRESETS.CUSTOM);

    const off = applyZoomCommand(normalizeZoomState({ autoFit: 'narrow', percent: 76, preset: 'fitWidth' }), 'setAutoFit', { mode: 'off' }, metrics);
    assert.equal(off.state.autoFit, 'off');
    assert.equal(off.state.scale, 1, 'turning the fit off after it was applied returns the page to 100 percent');
    assert.equal(off.state.preset, ZOOM_PRESETS.CUSTOM);
});

test('applyAutoFit re-resolves a narrow state and reports unchanged for an off state', () => {
    assert.equal(isZoomCommand('applyAutoFit'), true);
    assert.equal(isZoomCommand('setAutoFit'), true);
    assert.equal(isZoomCommand('resetZoom'), true);

    const narrow = applyZoomCommand(normalizeZoomState({ autoFit: 'narrow' }), 'applyAutoFit', null, { ...A4, viewportWidth: 700 });
    assert.equal(narrow.changed, true);
    assert.equal(narrow.state.preset, ZOOM_PRESETS.FIT_WIDTH);
    assert.equal(narrow.state.autoFit, 'narrow', 'the automatic fit keeps the mode');

    const again = applyZoomCommand(narrow.state, 'applyAutoFit', null, { ...A4, viewportWidth: 700 });
    assert.equal(again.changed, false);

    const off = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 130 }), 'applyAutoFit', null, { ...A4, viewportWidth: 700 });
    assert.equal(off.changed, false);
    assert.equal(off.state.scale, 1.3);
});

// ── Phase 11 fix round ─────────────────────────────────────────────────────

test('resetZoom takes the configured mode from the command context (metrics.autoFitMode), an explicit mode wins', () => {
    const metrics = { ...A4, viewportWidth: 650 };
    const fromContext = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 150 }), 'resetZoom', null, { ...metrics, autoFitMode: 'narrow' });
    assert.equal(fromContext.state.autoFit, 'narrow');
    assert.equal(fromContext.state.preset, ZOOM_PRESETS.FIT_WIDTH);

    const noContext = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 150 }), 'resetZoom', null, metrics);
    assert.equal(noContext.state.autoFit, 'off', 'a bare engine with no configured mode resets to a plain 100 percent');
    assert.equal(noContext.state.scale, 1);

    const explicit = applyZoomCommand(normalizeZoomState({ autoFit: 'off', percent: 150 }), 'resetZoom', { mode: 'always' }, { ...metrics, autoFitMode: 'narrow' });
    assert.equal(explicit.state.autoFit, 'always');
});

test('fitWidth is the reader asking for the fit: the mode is always "always", never an argument', () => {
    const fit = applyZoomCommand(normalizeZoomState({ autoFit: 'narrow', percent: 100 }), 'fitWidth', { autoFit: 'narrow' }, { ...A4, viewportWidth: 650 });
    assert.equal(fit.state.autoFit, 'always');
});
