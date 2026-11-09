// TmSegmentedControl.razor.js — the sliding indicator of the core segmented control (FZE-2).
// The module is otherwise covered only through the browser lane, so a pure-logic regression in
// the measurement, the data-tm-indicator claim rules or the ResizeObserver bookkeeping would
// need a full Playwright run to surface. The REAL ES module is imported once with hand-rolled
// DOM globals: a fake HTMLElement class (the module gates on `instanceof HTMLElement`), a fake
// ResizeObserver that records its targets, and trays/options built per test — the module's only
// shared state is a WeakMap keyed by the tray element, so fresh elements keep tests isolated.
import test from 'node:test';
import assert from 'node:assert/strict';
import { pathToFileURL, fileURLToPath } from 'node:url';
import path from 'node:path';

// ---- DOM fakes installed before the module is imported -------------------------------

class FakeHTMLElement {
    constructor() {
        this._attrs = new Map();
        this._focusCount = 0;
        this.scrollIntoViewCalls = [];
        this.offsetWidth = 0;
        this.offsetHeight = 0;
        this.offsetLeft = 0;
        this.offsetTop = 0;
        const style = {};
        Object.defineProperty(style, 'removeProperty', {
            value: (name) => { delete style[name]; },
            enumerable: false,
        });
        this.style = style;
    }

    getAttribute(name) { return this._attrs.has(name) ? this._attrs.get(name) : null; }
    setAttribute(name, value) { this._attrs.set(name, String(value)); }
    removeAttribute(name) { this._attrs.delete(name); }
    focus() { this._focusCount++; }
    scrollIntoView(options) { this.scrollIntoViewCalls.push(options); }
}

const createdObservers = [];

class FakeResizeObserver {
    constructor(callback) {
        this.callback = callback;
        this.targets = [];
        this.disconnectCount = 0;
        createdObservers.push(this);
    }

    observe(target) { this.targets.push(target); }
    disconnect() { this.disconnectCount++; }
}

globalThis.HTMLElement = FakeHTMLElement;
globalThis.ResizeObserver = FakeResizeObserver;

const moduleUrl = pathToFileURL(path.join(path.dirname(fileURLToPath(import.meta.url)), '..', 'TmSegmentedControl.razor.js'));
const segmented = await import(moduleUrl.href);

// ---- Fixture builders ------------------------------------------------------------------

function option({ selected = 'none', width = 40, height = 24, left = 0, top = 0 } = {}) {
    const o = new FakeHTMLElement();
    if (selected === 'checked') o.setAttribute('aria-checked', 'true');
    if (selected === 'selected') o.setAttribute('aria-selected', 'true');
    o.offsetWidth = width;
    o.offsetHeight = height;
    o.offsetLeft = left;
    o.offsetTop = top;
    return o;
}

function tray(options, { withIndicator = true } = {}) {
    const t = new FakeHTMLElement();
    t._options = options;
    t._indicator = withIndicator ? new FakeHTMLElement() : null;
    t.querySelectorAll = (selector) => selector === '.tm-segmented__option' ? [...t._options] : [];
    t.querySelector = (selector) => selector === '.tm-segmented__indicator' ? t._indicator : null;
    return t;
}

function trayWithSelection(overrides = {}) {
    const selected = option({ selected: 'checked', width: 56, height: 30, left: 12, top: 3, ...overrides });
    const other = option({ width: 40, height: 24, left: 68, top: 0 });
    return { tray: tray([other, selected]), selected, other };
}

const indicatorAttr = (t) => t.getAttribute('data-tm-indicator');

// ---- place: measurement + the data-tm-indicator claim rules ----------------------------

test('place sizes and transforms the indicator from the selected segment and claims sliding', () => {
    const { tray: t, selected } = trayWithSelection();

    segmented.place(t);

    const indicator = t._indicator;
    assert.equal(indicator.style.width, `${selected.offsetWidth}px`);
    assert.equal(indicator.style.height, `${selected.offsetHeight}px`);
    assert.equal(indicator.style.transform, `translate(${selected.offsetLeft}px, ${selected.offsetTop}px)`);
    assert.equal(indicatorAttr(t), 'sliding', 'the sliding mode is claimed only after a successful measurement');
});

test('place honors aria-selected when no aria-checked segment exists', () => {
    const selected = option({ selected: 'selected', width: 64, height: 28, left: 8, top: 2 });
    const t = tray([option({}), selected]);

    segmented.place(t);

    assert.equal(t._indicator.style.width, '64px');
    assert.equal(indicatorAttr(t), 'sliding');
});

test('place with nothing measurable gives the tray back to the CSS fallback', () => {
    // No selection at all.
    const t1 = tray([option({}), option({})]);
    t1._indicator.style.width = '99px'; // stale state from a previous successful place
    t1._indicator.style.transform = 'translate(1px, 1px)';
    segmented.place(t1);
    assert.equal(indicatorAttr(t1), null, 'the sliding claim is removed when there is no selection');
    assert.equal(t1._indicator.style.width, undefined, 'stale width is cleared');
    assert.equal(t1._indicator.style.height, undefined, 'stale height is cleared');
    assert.equal(t1._indicator.style.transform, undefined, 'stale transform is cleared');

    // A selected segment inside a hidden (zero-width) tray must not claim sliding either.
    const { tray: t2 } = trayWithSelection({ width: 0 });
    segmented.place(t2);
    assert.equal(indicatorAttr(t2), null, 'a zero-width segment measures nothing — no claim');
});

test('place without an indicator element or on a non-element tray never claims and never throws', () => {
    const { tray: noIndicator } = trayWithSelection({ withIndicator: false });
    assert.doesNotThrow(() => segmented.place(noIndicator));
    assert.doesNotThrow(() => segmented.place(null));
    assert.doesNotThrow(() => segmented.place({ querySelector: () => { throw new Error('would run'); } }));

    const selected = option({ selected: 'checked', width: 56, height: 30, left: 12, top: 3 });
    const t = tray([selected], { withIndicator: false });
    segmented.place(t);
    assert.equal(indicatorAttr(t), null);
});

// ---- focusOption: roving-tabindex focus + scroll-into-view -----------------------------

test('focusOption focuses the segment and scrolls a visible one into view', () => {
    const first = option({ width: 40 });
    const second = option({ width: 40 });
    const t = tray([first, second]);

    segmented.focusOption(t, 1);

    assert.equal(first._focusCount, 0);
    assert.equal(second._focusCount, 1, 'the segment at the index receives DOM focus');
    assert.deepEqual(second.scrollIntoViewCalls, [{ block: 'nearest', inline: 'nearest' }]);
});

test('focusOption does not scroll a hidden segment but still focuses it', () => {
    const hidden = option({ width: 0 });
    const t = tray([hidden]);

    segmented.focusOption(t, 0);

    assert.equal(hidden._focusCount, 1, 'focus still moves (the browser keeps it reachable)');
    assert.equal(hidden.scrollIntoViewCalls.length, 0, 'scrolling a zero-width box is pointless');
});

test('focusOption with an out-of-range index, a null tray or a non-element tray is a safe no-op', () => {
    const only = option({ width: 40 });
    const t = tray([only]);

    assert.doesNotThrow(() => segmented.focusOption(t, 5));
    assert.doesNotThrow(() => segmented.focusOption(t, -1));
    assert.doesNotThrow(() => segmented.focusOption(null, 0));
    assert.doesNotThrow(() => segmented.focusOption({}, 0));
    assert.equal(only._focusCount, 0);
});

// ---- claim then removal: the sliding mode tracks measurability -------------------------

test('the sliding claim is removed when the selected segment later disappears', () => {
    const { tray: t, selected } = trayWithSelection();

    segmented.place(t);
    assert.equal(indicatorAttr(t), 'sliding');

    selected.setAttribute('aria-checked', 'false');
    segmented.place(t);

    assert.equal(indicatorAttr(t), null, 'no selection → the tray falls back to the CSS surface');
    assert.equal(t._indicator.style.transform, undefined);
});

// ---- ResizeObserver bookkeeping per tray ----------------------------------------------

test('place attaches one observer per tray, watching the tray and every segment', () => {
    const before = createdObservers.length;
    const { tray: t, selected, other } = trayWithSelection();

    segmented.place(t);

    assert.equal(createdObservers.length - before, 1, 'exactly one observer is created');
    const observer = createdObservers[createdObservers.length - 1];
    assert.equal(observer.targets.length, 3, 'the tray and both segments are observed');
    assert.ok(observer.targets.includes(t));
    assert.ok(observer.targets.includes(selected));
    assert.ok(observer.targets.includes(other));
});

test('a second place does not stack observers on the same tray', () => {
    const before = createdObservers.length;
    const { tray: t } = trayWithSelection();

    segmented.place(t);
    segmented.place(t);

    assert.equal(createdObservers.length - before, 1, 'the per-tray WeakMap guards re-observation');
});

test('release disconnects the observer once and lets a later place observe again', () => {
    const { tray: t } = trayWithSelection();
    segmented.place(t);
    const observer = createdObservers[createdObservers.length - 1];

    segmented.release(t);
    assert.equal(observer.disconnectCount, 1, 'the tray observer is disconnected');

    segmented.release(t);
    assert.equal(observer.disconnectCount, 1, 'release is idempotent — no second disconnect');

    const before = createdObservers.length;
    segmented.place(t);
    assert.equal(createdObservers.length - before, 1, 'the released tray can be observed again');
    assert.notEqual(createdObservers[createdObservers.length - 1], observer);
});

test('release on a tray that was never observed and on null is a safe no-op', () => {
    assert.doesNotThrow(() => segmented.release(tray([option({})])));
    assert.doesNotThrow(() => segmented.release(null));
});

test('the observer re-places on resize: a geometry change re-measures the selection', () => {
    const { tray: t, selected } = trayWithSelection();
    segmented.place(t);
    const observer = createdObservers[createdObservers.length - 1];
    assert.equal(t._indicator.style.transform, `translate(${selected.offsetLeft}px, ${selected.offsetTop}px)`);

    selected.offsetWidth = 80;
    selected.offsetLeft = 20;
    observer.callback();

    assert.equal(t._indicator.style.width, '80px');
    assert.equal(t._indicator.style.transform, 'translate(20px, 3px)');
    assert.equal(indicatorAttr(t), 'sliding', 'still claimed after the re-measure');
});
