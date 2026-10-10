// Toolbar module tests with hand-rolled DOM stubs (no jsdom): the collapse maths (which buttons
// leave first and how many fit), the roving-tabindex target list and the measurement of a stub bar.
import test from 'node:test';
import assert from 'node:assert/strict';
import {
    collapseOrder, chooseVisibleCount, nextRovingIndex, rovingItems, computeFit, __resetForTests,
} from '../tm-toolbar.js';

test.beforeEach(() => __resetForTests());

// ── collapseOrder: the SAME rule as ActionOverflowLayout.Partition (lowest rank first, ties: last first)

test('collapseOrder drops the lowest rank first and the later item first within a rank', () => {
    // ranks: Primary=2, Secondary=1 -> Partition drops idx3 then idx1 (Secondary, last first), then idx2, idx0.
    assert.deepEqual(collapseOrder([2, 1, 2, 1]), [3, 1, 2, 0]);
});

test('collapseOrder of equal ranks starts at the end', () => {
    assert.deepEqual(collapseOrder([1, 1, 1]), [2, 1, 0]);
});

test('collapseOrder of nothing is nothing', () => {
    assert.deepEqual(collapseOrder([]), []);
});

// ── chooseVisibleCount: how many non-pinned buttons the bar can hold ─────────────────────────

const btn = (width, rank) => ({ width, rank });

test('everything fits -> every button stays', () => {
    const items = [btn(40, 2), btn(40, 1), btn(40, 2)];
    assert.equal(chooseVisibleCount(items, 200, 8, 44, false), 3);
});

test('exactly fitting counts as fitting (gaps included)', () => {
    const items = [btn(40, 2), btn(40, 1)];
    assert.equal(chooseVisibleCount(items, 88, 8, 44, false), 2);
    // One short of fitting: a button + the trigger (40 + 8 + 44 = 92) is the next-best layout.
    assert.equal(chooseVisibleCount(items, 87, 8, 44, false), 0);
    assert.equal(chooseVisibleCount(items, 92, 8, 44, false), 1);
});

test('a collapse reserves room for the More trigger', () => {
    // 3 x 40 + 2 gaps = 136 > 130 -> something must leave -> the trigger (44 + gap 8) takes room:
    // 2 x 40 + 1 gap + 52 = 140 > 130 -> 1 button + 52 = 92 <= 130.
    const items = [btn(40, 2), btn(40, 2), btn(40, 1)];
    assert.equal(chooseVisibleCount(items, 130, 8, 44, false), 1);
});

test('a bar with a pinned overflow-only button always reserves the trigger', () => {
    const items = [btn(40, 2), btn(40, 2)];
    // 88 fits the buttons alone but not buttons + trigger (88 + 52 = 140).
    assert.equal(chooseVisibleCount(items, 100, 8, 44, true), 1);
    assert.equal(chooseVisibleCount(items, 140, 8, 44, true), 2);
});

test('wide buttons leave the same buttons the C# partition would pick', () => {
    // Collapse order [Secondary(last), Secondary, Primary(last), Primary]: dropping the first two
    // frees enough room, so the count is 2 - the C# side then drops exactly those two.
    const items = [btn(60, 2), btn(60, 1), btn(60, 2), btn(60, 1)];
    assert.equal(chooseVisibleCount(items, 190, 8, 44, false), 2);
});

test('nothing fits -> zero (the C# partition keeps one button anyway)', () => {
    assert.equal(chooseVisibleCount([btn(300, 2)], 100, 8, 44, false), 0);
});

test('no buttons -> zero', () => {
    assert.equal(chooseVisibleCount([], 100, 8, 44, false), 0);
});

// ── keyboard: roving tabindex ────────────────────────────────────────────────────────────────

test('nextRovingIndex follows the reading direction and wraps', () => {
    assert.equal(nextRovingIndex(3, 0, 'ArrowRight', false), 1);
    assert.equal(nextRovingIndex(3, 2, 'ArrowRight', false), 0);
    assert.equal(nextRovingIndex(3, 0, 'ArrowLeft', false), 2);
    assert.equal(nextRovingIndex(3, 1, 'Home', false), 0);
    assert.equal(nextRovingIndex(3, 1, 'End', false), 2);
});

test('nextRovingIndex swaps the arrows in a right-to-left toolbar', () => {
    assert.equal(nextRovingIndex(3, 0, 'ArrowLeft', true), 1);
    assert.equal(nextRovingIndex(3, 0, 'ArrowRight', true), 2);
});

test('nextRovingIndex ignores vertical arrows and other keys', () => {
    assert.equal(nextRovingIndex(3, 0, 'ArrowDown', false), -1);
    assert.equal(nextRovingIndex(3, 0, 'a', false), -1);
    assert.equal(nextRovingIndex(0, -1, 'ArrowRight', false), -1);
});

function control({ disabled = false, ariaDisabled = false, blocked = false, label = '' } = {}) {
    return {
        label,
        disabled,
        getAttribute: name => (name === 'aria-disabled' && ariaDisabled ? 'true' : null),
        // rovingItems asks ONE closest() for every kind of exclusion: collapsed bar items, the
        // open More menu and its panel.
        closest: selector => {
            assert.match(selector, /tm-toolbar-item--collapsed/);
            assert.match(selector, /role="menu"/);
            return blocked ? {} : null;
        },
    };
}

test('rovingItems skips disabled, collapsed and in-menu controls (KeyboardNavigation_SkipsHiddenItems)', () => {
    const controls = [
        control({ label: 'a' }),
        control({ label: 'b', disabled: true }),
        control({ label: 'c', blocked: true }),
        control({ label: 'd', ariaDisabled: true }),
        control({ label: 'e' }),
    ];
    const root = { querySelectorAll: () => controls };
    assert.deepEqual(rovingItems(root).map(c => c.label), ['a', 'e']);
});

// ── computeFit: measuring a stub bar ─────────────────────────────────────────────────────────

function child({ width, rank, pin = 'auto', item = true, more = false }) {
    return {
        offsetWidth: width,
        dataset: item ? { tmToolbarItem: '', rank: String(rank), pin } : {},
        classList: { contains: name => (more && name === 'tm-toolbar-more') },
    };
}

function stubBar(children, clientWidth) {
    globalThis.getComputedStyle = () => ({ columnGap: '8px', gap: '8px' });
    return { clientWidth, children };
}

test('computeFit measures the buttons, the fixed children and the trigger', () => {
    // 2 buttons (40 + 60) + a 1px divider + a More trigger (50): gap 8.
    const bar = stubBar([
        child({ width: 40, rank: 2 }),
        child({ width: 1, item: false }),
        child({ width: 60, rank: 1 }),
        child({ width: 50, item: false, more: true }),
    ], 400);
    const fit = computeFit(bar);
    assert.equal(fit.maxVisible, 2, 'everything fits in 400px');
});

test('computeFit subtracts the fixed children from the room the buttons get', () => {
    const bar = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 1 }),
        child({ width: 30, item: false }),
    ], 230);
    // 200 + 8 gap + 30 fixed + 8 gap = 246 > 230 -> Secondary leaves: 100 + 30 + gaps + trigger(44+8)=190 <= 230.
    assert.equal(computeFit(bar).maxVisible, 1);
});

test('computeFit never measures OverflowOnly buttons but reserves the trigger for them', () => {
    const bar = stubBar([
        child({ width: 100, rank: 2 }),
        child({ width: 100, rank: 0, pin: 'always' }),
    ], 130);
    const fit = computeFit(bar);
    assert.equal(fit.hasPinned, true);
    assert.equal(fit.maxVisible, 0, '100 + trigger 52 > 130');
});

test('computeFit reports a signature that changes only when the answer changes', () => {
    const wide = computeFit(stubBar([child({ width: 40, rank: 2 })], 400));
    const wider = computeFit(stubBar([child({ width: 40, rank: 2 })], 500));
    const narrow = computeFit(stubBar([child({ width: 40, rank: 2 }), child({ width: 40, rank: 1 })], 60));
    assert.equal(wide.signature, wider.signature);
    assert.notEqual(wide.signature, narrow.signature);
});
