// Node unit tests for the canvas FUNNEL bookkeeping — the AutoFilter header buttons
// (`drawFilterButtons`). The funnel is not cosmetic: the E2E filter lane and the "Filter" toolbar
// toggle both depend on the buttons this function collects into `state.filterButtons`, and the
// phase-7 coarse-pointer hit rect is resolved while they are pushed. A throw in here is invisible in
// the DOM (it happens inside a requestAnimationFrame paint) and only shows up as "the filter never
// enables", so the seam is pinned by a node test with a stub 2D context.
//
// Run: npm run test:spreadsheet
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const CANVAS_SCRIPT = new URL('../spreadsheet-canvas.js', import.meta.url);
const SOURCE = readFileSync(CANVAS_SCRIPT, 'utf8');

/** Loads the shipped canvas script into an isolated sandbox and returns `_internals`. */
function loadInternals() {
    const sandbox = {
        Element: class StubElement {},
        window: {
            matchMedia: () => ({ matches: false, addEventListener() {}, removeEventListener() {} }),
        },
        getComputedStyle: () => ({ getPropertyValue: () => '' }),
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
    assert.ok(internals, 'spreadsheet-canvas.js must expose window.tmSpreadsheetCanvas._internals');
    assert.equal(typeof internals.drawFilterButtons, 'function',
        '_internals.drawFilterButtons must be a function — the funnel bookkeeping is the node-tested seam');
    return internals;
}

/** A 2D context stub: every method is a no-op, every style assignment is stored. */
function stubContext() {
    const noop = () => undefined;
    return new Proxy({}, {
        get: (target, property) => (property in target ? target[property] : noop),
        set: (target, property, value) => {
            target[property] = value;
            return true;
        },
    });
}

const internals = loadInternals();

function makeModel(overrides = {}) {
    return {
        rowHeaderWidth: 40,
        columnHeaderHeight: 20,
        scrollLeft: 0,
        scrollTop: 0,
        freezeRowCount: 0,
        freezeColumnCount: 0,
        density: 'compact',
        pointerCoarse: false,
        cells: [],
        ...overrides,
    };
}

function makeRoot(model) {
    return {
        clientWidth: 800,
        clientHeight: 400,
        __tmSpreadsheetCanvas: {
            model,
            palette: {},
            metrics: null,
            sheetState: { cellStore: null },
            filterButtons: [],
        },
    };
}

const filterCells = [
    { row: 0, col: 3, left: 100, top: 0, width: 90, height: 20, filter: { active: false } },
    { row: 0, col: 4, left: 190, top: 0, width: 90, height: 20, filter: { active: true } },
];

test('drawFilterButtons_CollectsOneButtonPerFilteredHeaderCell', () => {
    const model = makeModel();
    const root = makeRoot(model);

    // A throw here aborts the paint AFTER the state was cleared, which is exactly the
    // "the Filter toggle does nothing" symptom the E2E lane reported.
    internals.drawFilterButtons(stubContext(), root, model, {}, filterCells);

    const buttons = root.__tmSpreadsheetCanvas.filterButtons;
    assert.equal(buttons.length, 2, 'both filtered header cells get a funnel');
    // The events live in the sandbox realm, so compare the values, never the array identity.
    assert.deepEqual(Array.from(buttons, button => button.col), [3, 4]);
    assert.equal(buttons[0].active, false);
    assert.equal(buttons[1].active, true, 'an active filter paints the pressed funnel');
    for (const button of buttons) {
        assert.ok(button.w > 0 && button.h > 0, 'the painted funnel has a size');
        assert.ok(button.hit, 'every button carries the hit rect the tap handler reads');
        assert.ok(button.hit.width >= button.w, 'the hit rect never shrinks the painted funnel');
    }
});

test('drawFilterButtons_ClearsThePreviousButtonsWhenTheFilterGoesAway', () => {
    const model = makeModel();
    const root = makeRoot(model);
    root.__tmSpreadsheetCanvas.filterButtons = [{ col: 9, x: 1, y: 1, w: 1, h: 1 }];

    internals.drawFilterButtons(stubContext(), root, model, {}, []);
    assert.equal(root.__tmSpreadsheetCanvas.filterButtons.length, 0,
        'clearing the AutoFilter must leave no funnel behind');
});

test('drawFilterButtons_CoarsePointer_ExpandsTheHitRectInsideItsColumn', () => {
    const model = makeModel({ pointerCoarse: true, density: 'comfortable' });
    const root = makeRoot(model);

    internals.drawFilterButtons(stubContext(), root, model, {}, [filterCells[0]]);

    const [button] = root.__tmSpreadsheetCanvas.filterButtons;
    assert.ok(button.hit.width >= 44, `a finger gets the 44px floor, was ${button.hit.width}`);
    assert.ok(button.hit.width <= filterCells[0].width,
        `the hit rect must stay inside its column (${button.hit.width} of ${filterCells[0].width})`);
    assert.ok(button.hit.y >= 20, 'the hit rect never climbs into the column header gutter');
});
