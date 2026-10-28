// Node unit tests for the Tempo.Blazor.Spreadsheet canvas engine's PURE helpers
// (redesign phase 2: tokens, density, header highlight). The browser module is a classic
// script that needs a `window` and a DOM only for its interaction paths, so the pure helpers
// are exercised in a `vm` sandbox: load the shipped source with a stubbed
// `getComputedStyle`/`window`, then assert on `window.tmSpreadsheetCanvas._internals`.
//
// Run: npm run test:spreadsheet
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const CANVAS_SCRIPT = new URL('../spreadsheet-canvas.js', import.meta.url);
const SOURCE = readFileSync(CANVAS_SCRIPT, 'utf8');

/**
 * Loads the shipped canvas script into an isolated sandbox. `tokens` is what
 * getComputedStyle(root) reports for a `var(--tm-*)` lookup, which is exactly how the browser
 * resolves a component token (custom properties are substituted at computed-value time).
 */
function loadCanvas(tokens = {}) {
    const root = { __tokens: tokens };
    const sandbox = {
        window: {},
        getComputedStyle: element => ({
            getPropertyValue: name => (element?.__tokens?.[name] ?? '').trim(),
        }),
        performance: { now: () => 0 },
        requestAnimationFrame: () => 0,
        cancelAnimationFrame: () => undefined,
        setTimeout: () => 0,
        clearTimeout: () => undefined,
        setInterval: () => 0,
        clearInterval: () => undefined,
        console,
    };
    vm.createContext(sandbox);
    vm.runInContext(SOURCE, sandbox, { filename: 'spreadsheet-canvas.js' });
    const internals = sandbox.window.tmSpreadsheetCanvas?._internals;
    assert.ok(
        internals,
        'spreadsheet-canvas.js must expose window.tmSpreadsheetCanvas._internals (palette, density, header highlight) for the node lane');
    return { internals, root };
}

/** The palette buildPalette produces for a set of resolved custom-property values. */
function paletteFor(tokens) {
    const { internals, root } = loadCanvas(tokens);
    return internals.buildPalette(root);
}

// ── buildPalette ────────────────────────────────────────────────────────────────────────────────

test('buildPalette_SelectionFill_FollowsToken', () => {
    const palette = paletteFor({
        '--tm-color-primary': '#6366f1',
        '--tm-spreadsheet-selection-border': 'rgb(4, 5, 6)',
        '--tm-spreadsheet-selection-fill': '#0a0b0c',
        '--tm-spreadsheet-selection-fill-alpha': '0.2',
    });

    assert.equal(palette.selectionBorder, 'rgb(4, 5, 6)');
    assert.equal(palette.selectionFill, '#0a0b0c');
    assert.equal(palette.selectionFillAlpha, 0.2);
    assert.ok(
        !palette.selectionFill.includes('37, 99, 235') && !palette.selectionFill.includes('#2563eb'),
        'the selection fill must not fall back to the old hardcoded blue literal');
});

test('buildPalette_SelectionFill_WithoutToken_DoesNotInventABlue', () => {
    const palette = paletteFor({ '--tm-color-primary': '#4f46e5' });

    assert.equal(palette.selectionFill, '#4f46e5',
        'a missing component token degrades to the primary token, never to the legacy literal');
    assert.equal(palette.selectionFillAlpha, 0.12, 'the default wash strength');
    assert.equal(palette.selectionBorder, '#4f46e5', 'the border defaults to the primary token');
});

test('buildPalette_RefColors_FromTokens', () => {
    const tokens = {
        '--tm-spreadsheet-ref-0': '#101010',
        '--tm-spreadsheet-ref-1': '#202020',
        '--tm-spreadsheet-ref-2': '#303030',
        '--tm-spreadsheet-ref-3': '#404040',
        '--tm-spreadsheet-ref-4': '#505050',
        '--tm-spreadsheet-ref-5': '#606060',
    };
    const palette = paletteFor(tokens);

    assert.equal(palette.formulaRefs.length, 6);
    for (let index = 0; index < 6; index++) {
        const entry = palette.formulaRefs[index];
        assert.equal(entry.stroke, tokens[`--tm-spreadsheet-ref-${index}`]);
        assert.match(
            entry.fill,
            /^rgba\(\d+, \d+, \d+, 0\.08\)$/,
            `the ref-${index} wash must be derived from its token stroke, got ${entry.fill}`);
        assert.ok(
            !entry.stroke.includes('#1e8fe0')
            && !entry.stroke.includes('#e04030')
            && !entry.stroke.includes('#20a040')
            && !entry.stroke.includes('#a040c0')
            && !entry.stroke.includes('#e08020')
            && !entry.stroke.includes('#208080'),
            'the legacy hardcoded ref palette must be gone');
    }
});

test('buildPalette_RefColors_WithoutComponentTokens_FallBackToTheCategoryPalette', () => {
    const palette = paletteFor({
        '--tm-category-1': '#aaaaaa',
        '--tm-category-6': '#bbbbbb',
    });

    assert.equal(palette.formulaRefs[0].stroke, '#aaaaaa', 'ref-0 falls back to category-1');
    assert.equal(palette.formulaRefs[1].stroke, '#bbbbbb', 'ref-1 falls back to category-6');
});

test('buildPalette_DarkTokens_Propagate', () => {
    const palette = paletteFor({
        '--tm-color-surface': '#0f172a',
        '--tm-color-surface-elevated': '#1e293b',
        '--tm-color-text': '#f8fafc',
        '--tm-color-border': '#334155',
        '--tm-color-primary': '#818cf8',
        '--tm-spreadsheet-header-bg': '#1e293b',
        '--tm-spreadsheet-header-active-bg': 'rgba(129, 140, 248, 0.15)',
        '--tm-spreadsheet-gridline': '#334155',
    });

    assert.equal(palette.surface, '#0f172a');
    assert.equal(palette.elevated, '#1e293b');
    assert.equal(palette.text, '#f8fafc');
    assert.equal(palette.primary, '#818cf8');
    assert.equal(palette.headerBg, '#1e293b');
    assert.equal(palette.headerActiveBg, 'rgba(129, 140, 248, 0.15)');
    assert.equal(palette.gridline, '#334155');
});

test('buildPalette_HeaderTokens_ReplaceTheElevatedDefault', () => {
    const palette = paletteFor({
        '--tm-color-surface-elevated': '#f8fafc',
        '--tm-color-text-muted': '#64748b',
        '--tm-color-primary': '#2563eb',
        '--tm-spreadsheet-header-bg': '#f1f5f9',
        '--tm-spreadsheet-header-text': '#475569',
        '--tm-spreadsheet-header-active-text': '#1d4ed8',
        '--tm-spreadsheet-header-font': '500 11px Inter, sans-serif',
    });

    assert.equal(palette.headerBg, '#f1f5f9');
    assert.equal(palette.headerText, '#475569');
    assert.equal(palette.headerActiveText, '#1d4ed8');
    assert.equal(palette.headerFont, '500 11px Inter, sans-serif');
});

test('buildPalette_Gridline_FollowsToken', () => {
    const palette = paletteFor({
        '--tm-color-border-subtle': '#cbd5e1',
        '--tm-spreadsheet-gridline': '#abcdef',
    });
    assert.equal(palette.gridline, '#abcdef', 'the grid strokes --tm-spreadsheet-gridline');

    const fallback = paletteFor({ '--tm-color-border-subtle': '#cbd5e1' });
    assert.equal(fallback.gridline, '#cbd5e1',
        'without the component token the gridline degrades to the core border token');
});

test('buildPalette_DangerAndInverseTokens_ReplaceTheLegacyLiterals', () => {
    const palette = paletteFor({
        '--tm-color-danger': '#dc2626',
        '--tm-color-on-primary': '#ffffff',
        '--tm-color-warning': '#f59e0b',
        '--tm-text-link': '#2563eb',
    });

    assert.equal(palette.danger, '#dc2626');
    assert.equal(palette.inverseText, '#ffffff');
    assert.equal(palette.warning, '#f59e0b');
    assert.equal(palette.hyperlink, '#2563eb');
});

// ── resolveDensity (SP1) ───────────────────────────────────────────────────────────────────────

test('resolveDensity_Auto_CoarsePointer_IsComfortable', () => {
    const { internals } = loadCanvas();
    const comfortable = internals.resolveDensity('Auto', true);
    const compact = internals.resolveDensity('Auto', false);

    assert.equal(comfortable.density, 'comfortable');
    assert.equal(comfortable.comfortable, true);
    assert.equal(comfortable.rowScale, 1.5);
    assert.equal(comfortable.fontScale, 1.25);

    assert.equal(compact.density, 'compact');
    assert.equal(compact.comfortable, false);
    assert.equal(compact.rowScale, 1);
    assert.equal(compact.fontScale, 1);
});

test('resolveDensity_ExplicitValue_WinsOverThePointer', () => {
    const { internals } = loadCanvas();

    assert.equal(internals.resolveDensity('Comfortable', false).density, 'comfortable');
    assert.equal(internals.resolveDensity('Comfortable', false).rowScale, 1.5);
    assert.equal(internals.resolveDensity('Compact', true).density, 'compact');
    assert.equal(internals.resolveDensity('Compact', true).rowScale, 1);
});

test('resolveDensity_IsCaseInsensitive_AndUnknownFallsBackToAuto', () => {
    const { internals } = loadCanvas();

    assert.equal(internals.resolveDensity('comfortable', false).density, 'comfortable');
    assert.equal(internals.resolveDensity('COMPACT', true).density, 'compact');
    assert.equal(internals.resolveDensity(null, true).density, 'comfortable');
    assert.equal(internals.resolveDensity('nonsense', false).density, 'compact');
});

// ── resolveHeaderHighlight ────────────────────────────────────────────────────────────────────

test('resolveHeaderHighlight_ReturnsActiveRowsAndColumns', () => {
    const { internals } = loadCanvas();
    const highlight = internals.resolveHeaderHighlight({
        row: 4,
        col: 3,
        startRow: 4,
        startCol: 3,
        endRow: 6,
        endCol: 5,
    });

    assert.deepEqual({ ...highlight.activeRows }, { start: 4, end: 6 });
    assert.deepEqual({ ...highlight.activeColumns }, { start: 3, end: 5 });
    assert.equal(highlight.activeRow, 4);
    assert.equal(highlight.activeCol, 3);
});

test('resolveHeaderHighlight_NormalisesAReversedSelection', () => {
    const { internals } = loadCanvas();
    const highlight = internals.resolveHeaderHighlight({
        row: 1,
        col: 2,
        startRow: 9,
        startCol: 7,
        endRow: 1,
        endCol: 2,
    });

    assert.deepEqual({ ...highlight.activeRows }, { start: 1, end: 9 });
    assert.deepEqual({ ...highlight.activeColumns }, { start: 2, end: 7 });
});

test('resolveHeaderHighlight_WithoutASelection_FallsBackToTheActiveCell', () => {
    const { internals } = loadCanvas();

    const fromActive = internals.resolveHeaderHighlight({ row: 2, col: 4 });
    assert.deepEqual({ ...fromActive.activeRows }, { start: 2, end: 2 });
    assert.deepEqual({ ...fromActive.activeColumns }, { start: 4, end: 4 });

    const empty = internals.resolveHeaderHighlight(null);
    assert.equal(empty.activeRow, -1);
    assert.equal(empty.activeCol, -1);
    assert.equal(empty.hasSelection, false);
    assert.deepEqual({ ...empty.activeRows }, { start: -1, end: -1 });
});

// ── resolveHeaderStrips (item 3: a selection that extends past the viewport) ───────────────────

function frame(index, x, y, width, height) {
    return { index, Index: index, x, y, width, height };
}

test('resolveHeaderStrips_IntersectsTheVisibleFrames', () => {
    const { internals } = loadCanvas();
    const rows = [frame(10, 0, 40, 40, 30), frame(11, 0, 70, 40, 30), frame(12, 0, 100, 40, 30)];
    const columns = [frame(2, 40, 0, 64, 20), frame(3, 104, 0, 64, 20)];

    // A selection from A1 to ZZ9999 — only the visible frames may contribute a strip.
    const strips = internals.resolveHeaderStrips(
        { minRow: 0, maxRow: 9998, minCol: 0, maxCol: 701 }, rows, columns, 40, 20);

    assert.equal(strips.length, 2);
    const columnStrip = strips.find(strip => strip.height === 20);
    const rowStrip = strips.find(strip => strip.width === 40);
    assert.deepEqual({ ...columnStrip }, { x: 40, y: 0, width: 128, height: 20 });
    assert.deepEqual({ ...rowStrip }, { x: 0, y: 40, width: 40, height: 90 });
});

test('resolveHeaderStrips_SkipsAStripWithNoVisibleFrame', () => {
    const { internals } = loadCanvas();
    const rows = [frame(10, 0, 40, 40, 30)];
    const columns = [frame(2, 40, 0, 64, 20)];

    const onlyRows = internals.resolveHeaderStrips(
        { minRow: 0, maxRow: 20, minCol: 40, maxCol: 60 }, rows, columns, 40, 20);
    assert.equal(onlyRows.length, 1, 'a selection outside the visible columns still highlights the row strip');
    assert.equal(onlyRows[0].width, 40);

    const empty = internals.resolveHeaderStrips(
        { minRow: 50, maxRow: 60, minCol: 40, maxCol: 60 }, rows, columns, 40, 20);
    assert.equal(empty.length, 0, 'nothing visible intersects => no dirty rect');
});

test('resolveHeaderStrips_HandlesAMissingSelection', () => {
    const { internals } = loadCanvas();
    const strips = internals.resolveHeaderStrips(null, [frame(0, 0, 0, 40, 20)], [frame(0, 40, 0, 64, 20)], 40, 20);
    assert.equal(strips.length, 0, 'no selection => no strip (an array from the vm realm cannot be deep-compared)');
});

// ── resolveDensityFromModel (item 8: the frame is the single source of the scales) ────────────

test('resolveDensityFromModel_PrefersTheFrameScales', () => {
    const { internals } = loadCanvas();
    const resolved = internals.resolveDensityFromModel({
        density: 'Auto',
        pointerCoarse: true,
        densityRowScale: 1.4,
        densityFontScale: 1.1,
        densityHandleScale: 2,
    });

    assert.equal(resolved.density, 'comfortable');
    assert.equal(resolved.rowScale, 1.4, 'the component resolved the row scale, the canvas just reads it');
    assert.equal(resolved.fontScale, 1.1);
    assert.equal(resolved.handleScale, 2);
});

test('resolveDensityFromModel_WithoutFrameScales_FallsBackToTheJsRule', () => {
    const { internals } = loadCanvas();

    const comfortable = internals.resolveDensityFromModel({ density: 'Auto', pointerCoarse: true });
    assert.equal(comfortable.density, 'comfortable');
    assert.equal(comfortable.rowScale, 1.5);
    assert.equal(comfortable.fontScale, 1.25);
    assert.equal(comfortable.handleScale, 1.4);

    const compact = internals.resolveDensityFromModel({ density: 'Auto', pointerCoarse: false });
    assert.equal(compact.rowScale, 1);
});
