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
    // The guard helpers check `target instanceof Element`. Node has no DOM, so the sandbox gets a
    // minimal Element base plus the fixtures below; `closest` answers from a plain ancestor list.
    class StubElement {
        constructor(ancestors = []) {
            this.ancestors = ancestors;
        }
        closest(selector) {
            const wanted = selector.split(',').map(part => part.trim());
            for (const candidate of ['self', ...this.ancestors]) {
                if (wanted.includes(candidate)) return { selector: candidate };
            }
            return null;
        }
    }
    const sandbox = {
        Element: StubElement,
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

// ── Grid guards: dialogs own their own keys and wheel ───────────────────────────────────────────

function guards(tokens = {}) {
    return loadCanvas(tokens).internals;
}

test('isEditableKeyTarget_And_overlayGuard_ExportsExist', () => {
    const internals = guards();
    assert.equal(typeof internals.isEditableKeyTarget, 'function',
        'the grid keydown guard must be testable from the node lane');
    assert.equal(typeof internals.isSpreadsheetOverlayTarget, 'function');
});

test('isSpreadsheetOverlayTarget_RecognisesTheModalOverlaySelector', () => {
    // The selector list is the contract: a dialog rendered inside the grid root (the column-width /
    // row-height dialogs, every TmModal surface) must be recognised so Enter/Tab/Escape and wheel
    // never reach the grid's listeners. The node lane has no DOM, so the source is asserted instead
    // of a live element: the guard is a one-line closest() over exactly this list.
    const source = readFileSync(CANVAS_SCRIPT, 'utf8');
    const match = source.match(/function isSpreadsheetOverlayTarget\(target\) \{[\s\S]*?closest\(([^)]*)\)/);
    assert.ok(match, 'isSpreadsheetOverlayTarget must stay a closest() guard');
    assert.match(match[1], /\.tm-modal-overlay/, 'a core TmModal surface must count as an overlay');
    assert.match(match[1], /\.tm-spreadsheet-resize-dialog\b/);
    assert.doesNotMatch(match[1], /\.tm-spreadsheet-resize-dialog-backdrop/,
        'the package backdrop is gone — the selector is dead');
    const keydown = source.match(/const onKeyDown = ev => \{([\s\S]*?)\n\s{12}if \(s\.editor\)/);
    assert.ok(keydown, 'onKeyDown must stay findable');
    assert.match(keydown[1], /isEditableKeyTarget\(ev\.target\) \|\| isSpreadsheetOverlayTarget\(ev\.target\)/,
        'the keydown guard must skip editable fields AND overlay targets');
    const wheel = source.match(/const onWheel = ev => \{([\s\S]*?)\n\s{12}\/\/ Ctrl \+ wheel/);
    assert.ok(wheel, 'onWheel must stay findable');
    assert.match(wheel[1], /isSpreadsheetOverlayTarget\(ev\.target\)/,
        'a wheel over an open dialog must not scroll the grid behind it');
});

// ── Phase 7b: the density gutter contract, the header font scale and the live pointer change ───

test('resolveDensity_Comfortable_ReturnsGutterScale', () => {
    const { internals } = loadCanvas();
    const comfortable = internals.resolveDensity('Auto', true);
    const compact = internals.resolveDensity('Auto', false);

    assert.equal(comfortable.gutterScale, 1.4,
        'the JS rule must mirror SpreadsheetDensityScale.ComfortableGutterScale (40->56, 20->28)');
    assert.equal(compact.gutterScale, 1, 'a fine pointer keeps the historic 40/20 gutters');

    const forced = internals.resolveDensity('Comfortable', false);
    assert.equal(forced.gutterScale, 1.4, 'a forced Comfortable scales the gutters on a fine pointer too');
});

test('resolveDensityFromModel_PrefersTheFrameGutterScale', () => {
    const { internals } = loadCanvas();
    const resolved = internals.resolveDensityFromModel({
        density: 'Comfortable',
        densityRowScale: 1.5,
        densityFontScale: 1.25,
        densityHandleScale: 1.4,
        densityGutterScale: 1.6,
    });

    assert.equal(resolved.gutterScale, 1.6, 'the component owns the scale; the canvas only reads it');
    assert.equal(internals.resolveDensityFromModel({ density: 'Auto', pointerCoarse: false }).gutterScale, 1);
});

test('hitTest_UsesScaledGutters', () => {
    const { internals } = loadCanvas();
    // A primary press at client (10, 5) inside the grid body. The header gutters shift the content
    // origin, so the same press lands on a DIFFERENT cell in Comfortable than in Compact — this is the
    // bug class the phase-2 learning describes (cell centres moved silently).
    const press = { clientX: 66, clientY: 33, rectLeft: 0, rectTop: 0, scrollLeft: 0, scrollTop: 0 };

    const comfortable = internals.resolveContentPoint({
        ...press, rowHeaderWidth: 56, columnHeaderHeight: 28,
    });
    assert.deepEqual({ ...comfortable }, { x: 10, y: 5 },
        'the comfortable gutters (40x1.4 / 20x1.4) must be subtracted from the press point');

    const compact = internals.resolveContentPoint({
        ...press, rowHeaderWidth: 40, columnHeaderHeight: 20,
    });
    assert.deepEqual({ ...compact }, { x: 26, y: 13 },
        'the compact gutters are the historic 40/20, so the same screen point is 16px/8px further right/down');

    // The scroll offset is added back before the gutter subtraction, exactly like toContentPoint.
    const scrolled = internals.resolveContentPoint({
        clientX: 66, clientY: 33, rectLeft: 10, rectTop: 20, scrollLeft: 64, scrollTop: 30,
        rowHeaderWidth: 56, columnHeaderHeight: 28,
    });
    assert.deepEqual({ ...scrolled }, { x: 64, y: 15 });
});

test('resolveAutoFillHitSize_Coarse_IsTouchFloor_FineIs10', () => {
    const { internals } = loadCanvas();

    assert.equal(internals.resolveAutoFillHitSize({ handleScale: 1, coarse: false }), 10,
        'the compact fine pointer keeps exactly the pre-phase-7 10px hit box (no mouse regression)');
    assert.equal(internals.resolveAutoFillHitSize({ handleScale: 1.4, coarse: false }), 14,
        'Comfortable grows the hit box with the painted handle');
    assert.equal(internals.resolveAutoFillHitSize({ handleScale: 1, coarse: true }), 24,
        'a coarse pointer reaches the touch floor even at compact density');
    assert.equal(internals.resolveAutoFillHitSize({ handleScale: 1.4, coarse: true }), 24);
    assert.equal(internals.resolveAutoFillHitSize({}), 10, 'a missing input degrades to the historic box');
});

test('resolveFilterButtonHitRect_Coarse_IsTheTouchFloor_FineKeepsTheGlyph', () => {
    const { internals } = loadCanvas();
    const rect = { x: 96, y: 2, w: 12, h: 16 };

    const fine = internals.resolveFilterButtonHitRect({ rect, columnWidth: 64, headerHeight: 20, coarse: false });
    assert.deepEqual({ ...fine }, { x: 96, y: 2, width: 12, height: 16 },
        'a fine pointer keeps the painted glyph rect exactly');

    const coarse = internals.resolveFilterButtonHitRect({ rect, columnWidth: 64, headerHeight: 28, coarse: true });
    assert.equal(coarse.height, 28, 'the hit box fills the comfortable header gutter height');
    assert.ok(coarse.width >= 44, `the coarse hit box must reach the touch floor, got ${coarse.width}`);
    assert.ok(coarse.x >= 96 + 12 - 64 && coarse.x + coarse.width <= 96 + 12,
        'the hit box never leaves its own column');
});

test('headerFont_ScalesWithDensity', () => {
    const { internals } = loadCanvas();
    const token = '500 11px Inter, sans-serif';

    assert.equal(internals.resolveHeaderFont(token, 1), token, 'Compact keeps the token exactly (no rounding drift)');
    assert.equal(internals.resolveHeaderFont(token, 1.25), '500 13.75px Inter, sans-serif',
        'Comfortable scales the header font by the same fontScale the cells use');
    assert.equal(internals.resolveHeaderFont('600 10pt Inter', 1.5), '600 15pt Inter',
        'a pt shorthand scales in place, keeping the unit');
    assert.equal(internals.resolveHeaderFont('', 1.5), '', 'a missing token stays missing');
    assert.equal(internals.resolveHeaderFont(token, 0), token, 'a zero/invalid scale is ignored');
});

test('buildPalette_DarkActiveHeader_IsOpaque', () => {
    // What an UNREGISTERED custom property keeps: var() substituted, color-mix() NOT evaluated.
    const mix = paletteFor({
        '--tm-spreadsheet-header-bg': 'rgb(30, 41, 59)',
        '--tm-spreadsheet-header-active-bg': 'color-mix(in srgb, rgb(129, 140, 248) 22%, rgb(30, 41, 59))',
    });

    assert.equal(mix.headerActiveBgAlpha, 1,
        'the dark active header must be OPAQUE (the old rgba(..., 0.15) tint washed out over the dark header)');
    assert.match(mix.headerActiveBg, /^rgb\(/, `an opaque tint serialises as rgb(), got ${mix.headerActiveBg}`);
    assert.equal(mix.headerActiveBg, 'rgb(52, 63, 101)',
        '22 % of the primary over the header surface, mixed in srgb');

    // What Chromium serialises a computed color-mix() to (@property/color() hosts).
    const srgb = paletteFor({
        '--tm-spreadsheet-header-active-bg': 'color(srgb 0.2039 0.2471 0.3961)',
    });
    assert.equal(srgb.headerActiveBgAlpha, 1);
    assert.equal(srgb.headerActiveBg, 'rgb(52, 63, 101)');

    // A translucent token still reports its own alpha instead of claiming opacity.
    const translucent = paletteFor({
        '--tm-spreadsheet-header-active-bg': 'rgba(129, 140, 248, 0.15)',
    });
    assert.equal(translucent.headerActiveBgAlpha, 0.15);
    assert.equal(translucent.headerActiveBg, 'rgba(129, 140, 248, 0.15)',
        'a token the parser understands is normalised, never mangled');

    // An unparseable notation is passed through untouched and reports an UNKNOWN alpha.
    const relative = paletteFor({
        '--tm-spreadsheet-header-active-bg': 'rgb(from #6366f1 r g b / 0.15)',
    });
    assert.equal(relative.headerActiveBgAlpha, null,
        'an unparsed notation must not claim opacity it does not have');
    assert.equal(relative.headerActiveBg, 'rgb(from #6366f1 r g b / 0.15)');
});

test('resolveColor_ParsesTheNotationsATokenCanCarry', () => {
    const { internals } = loadCanvas();

    assert.deepEqual({ ...internals.parseColor('#6366f1') }, { r: 99, g: 102, b: 241, a: 1 });
    assert.deepEqual({ ...internals.parseColor('#63f') }, { r: 102, g: 51, b: 255, a: 1 });
    assert.deepEqual({ ...internals.parseColor('rgba(1, 2, 3, 0.5)') }, { r: 1, g: 2, b: 3, a: 0.5 });
    // Channels stay fractional from a color()/color-mix() notation (mixing precision), the FORMATTER
    // is what rounds them; the palette therefore serialises rgb()/rgba() with whole channels.
    const srgb = internals.parseColor('color(srgb 0.5 0.25 0.125 / 0.75)');
    assert.deepEqual({ r: Math.round(srgb.r), g: Math.round(srgb.g), b: Math.round(srgb.b), a: srgb.a },
        { r: 128, g: 64, b: 32, a: 0.75 });
    assert.equal(Math.round(internals.parseColor('color-mix(in srgb, #ffffff 50%, #000000)').r), 128);
    assert.equal(internals.parseColor('currentColor'), null, 'an unparseable value returns null, never NaN');
    assert.equal(internals.parseColor(''), null);
});

test('coarsePointerChange_RecomputesAutoDensity_OnlyForAuto', () => {
    const { internals } = loadCanvas();
    const seen = [];
    let handler = null;
    const query = {
        matches: false,
        addEventListener: (name, callback) => {
            if (name === 'change') handler = callback;
        },
    };

    const listener = internals.attachPointerKindListener(query, () => 'Auto', (density, coarse) => {
        seen.push({ density, coarse });
    });
    assert.ok(listener && handler, 'the observer must register exactly one change listener');
    handler({ matches: true });
    handler({ matches: false });
    assert.deepEqual(seen, [
        { density: 'comfortable', coarse: true },
        { density: 'compact', coarse: false },
    ], 'Auto follows the live media query (attaching a mouse to a tablet re-resolves the density)');

    const forcedSeen = [];
    let forcedHandler = null;
    const forcedQuery = {
        addEventListener: (name, callback) => {
            if (name === 'change') forcedHandler = callback;
        },
    };
    internals.attachPointerKindListener(forcedQuery, 'Compact', (density, coarse) => {
        forcedSeen.push({ density, coarse });
    });
    forcedHandler({ matches: true });
    assert.deepEqual(forcedSeen, [],
        'an explicit Density=Compact/Comfortable ignores the pointer change (no pointless .NET round-trip)');

    assert.equal(internals.attachPointerKindListener({}, 'Auto', () => { }), null,
        'a host without matchMedia degrades gracefully');
});

test('parseColor_NonFiniteChannelsOrAlpha_ReturnsNull', () => {
    const { internals } = loadCanvas();

    // `none` (and any other non-numeric component) must never leak NaN into fillStyle — the caller has
    // to keep the RAW token instead, so the browser resolves it and nothing paints garbage.
    assert.equal(internals.parseColor('color(srgb 0.5 none 0.25)'), null,
        'a `none` colour channel is not a number the canvas may paint');
    assert.equal(internals.parseColor('color(srgb 0.5 0.5 0.5 / none)'), null,
        'a `none` alpha must fall back to the raw token');
    assert.equal(internals.parseColor('rgb(255 0 0 / none)'), null);
    assert.equal(internals.parseColor('rgb(none 0 0)'), null);
    assert.equal(internals.parseColor('color(srgb 0.5 0.5 0.5)').a, 1,
        'a plain color(srgb) without an alpha stays opaque');
});

test('parseColor_ColorMix_RejectsTranslucentOperandsAndShortWeights', () => {
    const { internals } = loadCanvas();

    assert.equal(internals.parseColor('color-mix(in srgb, rgba(10, 20, 30, 0.5) 22%, #1e293b)'), null,
        'a translucent operand cannot be mixed into an opaque strip — keep the raw token');
    assert.equal(internals.parseColor('color-mix(in srgb, #ffffff 20%, #000000 20%)'), null,
        'weights that add up to less than 100 % are ambiguous — keep the raw token');

    const full = internals.parseColor('color-mix(in srgb, #ffffff 20%, #000000 80%)');
    assert.ok(full, 'an opaque mix that adds up to 100 % is still resolved');
    assert.equal(Math.round(full.r), 51);

    const implicit = internals.parseColor('color-mix(in srgb, #ffffff 50%, #000000)');
    assert.ok(implicit, 'an implicit complement weight (50 % + 50 %) is a full 100 % mix');
});

test('resolveGridGutters_WithoutAModel_ReturnsNull', () => {
    const { internals } = loadCanvas();

    assert.equal(internals.resolveGridGutters({}), null,
        'a root without engine state must NOT report a 40/20 fallback the canvas never painted');
    assert.equal(internals.resolveGridGutters(null), null);

    const gutters = internals.resolveGridGutters({
        __tmSpreadsheetCanvas: { model: { RowHeaderWidth: 56, ColumnHeaderHeight: 28, DensityResolved: 'Comfortable' } },
    });
    assert.equal(gutters.rowHeaderWidth, 56);
    assert.equal(gutters.columnHeaderHeight, 28);
    assert.equal(gutters.density, 'Comfortable');
});
