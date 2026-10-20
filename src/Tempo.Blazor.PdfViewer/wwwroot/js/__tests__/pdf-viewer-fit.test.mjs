// pdf-viewer-fit.mjs tests — the pure fit-mode logic used by fitScale/_recomputeFit
// (call-time mode resolution for the ResizeObserver recompute + the fit scale math).
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    FIT_MIN_SCALE,
    FIT_MAX_SCALE,
    resolveRecomputeFitMode,
    isRecomputeValid,
    clampFitScale,
    computeFitScale
} from '../pdf-viewer-fit.mjs';

test('fit bounds mirror the component scale range', () => {
    assert.equal(FIT_MIN_SCALE, 0.1);
    assert.equal(FIT_MAX_SCALE, 5);
});

test('recompute resolves the CURRENT fit mode, not one captured at registration', () => {
    // The observer is registered while fit-width is active...
    const state = { pdfDoc: {}, fitMode: 'fit-width' };
    assert.equal(resolveRecomputeFitMode(state), 'fit-width');

    // ...then the user picks fit-page: the container (and thus the observer) is the
    // SAME element, so a mode captured at registration would pin fit-width forever.
    state.fitMode = 'fit-page';
    assert.equal(resolveRecomputeFitMode(state), 'fit-page');
    assert.notEqual(resolveRecomputeFitMode(state), 'fit-width');
});

test('recompute is skipped without a document or an active fit mode', () => {
    assert.equal(resolveRecomputeFitMode(undefined), null);
    assert.equal(resolveRecomputeFitMode({}), null);
    // clearFit ran: the mode returned to Custom — nothing to recompute.
    assert.equal(resolveRecomputeFitMode({ pdfDoc: {}, fitMode: null }), null);
    // Still loading: the mode was requested but the document is not there yet.
    assert.equal(resolveRecomputeFitMode({ fitMode: 'fit-width' }), null);
});

test('recompute is invalid when the mode changed during the async page lookup', () => {
    const state = { fitMode: 'fit-width' };
    assert.equal(isRecomputeValid(state, 'fit-width'), true);
    state.fitMode = 'fit-page';
    assert.equal(isRecomputeValid(state, 'fit-width'), false, 'a mode switch invalidates the pending recompute');
    assert.equal(isRecomputeValid(state, 'fit-page'), true);
    assert.equal(isRecomputeValid(undefined, 'fit-width'), false);
    assert.equal(isRecomputeValid({ fitMode: null }, 'fit-width'), false, 'clearFit invalidates the recompute');
});

test('clampFitScale bounds the ratio to the absolute fit range', () => {
    assert.equal(clampFitScale(0.001), FIT_MIN_SCALE);
    assert.equal(clampFitScale(50), FIT_MAX_SCALE);
    assert.equal(clampFitScale(0.732), 0.732, 'an exact container ratio beats the zoom steps');
});

test('computeFitScale fits the width', () => {
    assert.equal(computeFitScale({ width: 600, height: 800 }, 300, null, 'fit-width'), 0.5);
    assert.equal(computeFitScale({ width: 600, height: 800 }, 1200, null, 'fit-width'), 2);
});

test('computeFitScale fits the whole page through the smaller ratio', () => {
    // Landscape page in a portrait-ish container: height wins.
    assert.equal(computeFitScale({ width: 800, height: 400 }, 400, 300, 'fit-page'), 0.5);
    // Width wins when the container is narrow.
    assert.equal(computeFitScale({ width: 400, height: 800 }, 200, 3000, 'fit-page'), 0.5);
});

test('computeFitScale clamps the fit-page result to the absolute bounds', () => {
    assert.equal(computeFitScale({ width: 10, height: 10 }, 1000, 1000, 'fit-page'), FIT_MAX_SCALE);
    assert.equal(computeFitScale({ width: 10000, height: 10000 }, 10, 10, 'fit-page'), FIT_MIN_SCALE);
});

test('computeFitScale returns 0 when there is no usable space', () => {
    assert.equal(computeFitScale({ width: 600, height: 800 }, 0, null, 'fit-width'), 0);
    assert.equal(computeFitScale({ width: 600, height: 800 }, -5, null, 'fit-width'), 0);
    assert.equal(computeFitScale({ width: 600, height: 800 }, 300, 0, 'fit-page'), 0);
    assert.equal(computeFitScale({ width: 600, height: 800 }, 300, -1, 'fit-page'), 0);
    assert.equal(computeFitScale(null, 300, null, 'fit-width'), 0);
});

test('computeFitScale fit-width ignores the passed height', () => {
    assert.equal(computeFitScale({ width: 600, height: 800 }, 300, 0, 'fit-width'), 0.5);
});
