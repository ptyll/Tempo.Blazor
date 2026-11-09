// tm-animated-number.js — the rAF ticker behind TmAnimatedNumber (FZE-2). The module is
// otherwise covered only through the browser lane, so a pure-logic regression in the easing
// math, the formatter cache or the cancel path would need a full Playwright run to surface.
// The REAL module source runs in a fresh vm sandbox per test: the module-level WeakMaps
// (running tickers, painted values) and the bounded formatter cache start empty, the frame
// clock is a hand-driven fake queue, and matchMedia/performance are whatever the scenario
// needs. `export ` prefixes are stripped because a vm context is a script, not a module.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const source = readFileSync(fileURLToPath(new URL('../tm-animated-number.js', import.meta.url)), 'utf8');

/**
 * Loads the real module source into a fresh sandbox.
 * @param {object} [host] host seams: matchMedia, raf (false = no frame clock), document;
 *                        the frame clock is a fake whose time only advances through pump().
 */
function load({ matchMedia, raf = true, document } = {}) {
    const pending = new Map();
    const cancelled = [];
    let nextFrameId = 1;
    let clock = 0;
    const sandbox = {
        console,
        matchMedia,
        performance: { now: () => clock },
        document,
    };
    if (raf) {
        sandbox.requestAnimationFrame = (fn) => { const id = nextFrameId++; pending.set(id, fn); return id; };
        sandbox.cancelAnimationFrame = (id) => { cancelled.push(id); pending.delete(id); };
    }
    sandbox.globalThis = sandbox;

    const probe = '\n;globalThis.__mod = { animate, cancel, cancelAll, prefersReducedMotion };'
        + '\n;globalThis.__internals = { formatters, paintedValues };';
    vm.runInNewContext(source.replace(/^export /gm, '') + probe, sandbox, { filename: 'tm-animated-number.js' });
    return {
        ...sandbox.__mod,
        internals: sandbox.__internals,
        cancelled,
        pendingCount: () => pending.size,
        /** Advances the fake clock to `timestamp` and runs every frame queued so far. */
        pump(timestamp) {
            clock = Math.max(clock, timestamp);
            const batch = [...pending.values()];
            pending.clear();
            for (const fn of batch) fn(timestamp);
        },
    };
}

/** A value span the component rendered: records every textContent write. */
function valueEl() {
    const el = { isConnected: true, _text: '', paints: 0 };
    Object.defineProperty(el, 'textContent', {
        get() { return this._text; },
        set(v) { this._text = String(v); this.paints++; },
    });
    return el;
}

test('eases with the cubic out curve and lands the exact target on the last frame', () => {
    const env = load();
    const el = valueEl();

    env.animate(el, 0, 800, 1000, { maximumFractionDigits: 3 });

    env.pump(0);
    assert.equal(el.textContent, '0', 'first frame paints the start');

    env.pump(500); // progress 0.5 → eased 1 - 0.5³ = 0.875
    assert.ok(Math.abs(parseFloat(el.textContent.replace(/,/g, '')) - 700) < 1e-6, 'half time is past half value (cubic out)');

    env.pump(750); // progress 0.75 → eased 1 - 0.25³ = 0.984375
    assert.ok(Math.abs(parseFloat(el.textContent.replace(/,/g, '')) - 787.5) < 1e-6, 'late frames crawl towards the target');

    env.pump(1000);
    assert.equal(el.textContent, '800', 'the last frame is the exact target, not a rounding step');
    assert.equal(env.pendingCount(), 0, 'a finished loop does not schedule another frame');
});

test('exactly one loop per element: a retarget cancels the old loop and continues from the painted number', () => {
    const env = load();
    const el = valueEl();

    env.animate(el, 0, 1000, 1000, { maximumFractionDigits: 3 });
    env.pump(500); // painted 875
    const paintsAtRetarget = el.paints;
    assert.equal(parseFloat(el.textContent.replace(/,/g, '')), 875);

    // from = null: continue from what is on screen, jump onto the new target.
    env.animate(el, null, 2000, 1000, { maximumFractionDigits: 3 });
    assert.equal(env.pendingCount(), 1, 'one pending frame — the retargeted loop, not two');

    env.pump(1000); // the new run began at clock 500, so 500ms elapsed of its 1000ms
    assert.ok(Math.abs(parseFloat(el.textContent.replace(/,/g, '')) - (875 + 1125 * 0.875)) < 1e-6,
        'the new loop starts at 875, not at the old target or the new one');
    assert.equal(el.paints, paintsAtRetarget + 1, 'the stale loop painted nothing');
});

test('from = null continues from a ticker that already finished', () => {
    const env = load();
    const el = valueEl();

    env.animate(el, 5, 10, 0, {}); // zero duration paints immediately
    assert.equal(el.textContent, '10');
    assert.equal(env.internals.paintedValues.get(el), 10);

    env.animate(el, null, 20, 1000, { maximumFractionDigits: 3 });
    env.pump(250); // progress 0.25 → eased ≈ 0.578 → 10 + 10 * 0.578 ≈ 15.78
    assert.ok(parseFloat(el.textContent.replace(/,/g, '')) > 10 && parseFloat(el.textContent.replace(/,/g, '')) < 20,
        'continues from the painted 10 instead of jumping to 20');
});

test('the formatter cache keys by the normalized shape and keeps prefix/suffix outside the key', () => {
    const env = load();
    const el = valueEl();
    const paint = (options) => env.animate(el, 1, 1, 0, options);

    paint({ locale: 'en-US', maximumFractionDigits: 2 });
    assert.equal(env.internals.formatters.size, 1);
    paint({ locale: 'en-US', maximumFractionDigits: 2, prefix: '$', suffix: ' kg' });
    assert.equal(env.internals.formatters.size, 1, 'prefix/suffix are not part of the cache key');
    assert.equal(el.textContent, '$1 kg');
    paint({ locale: 'cs-CZ', maximumFractionDigits: 2 });
    assert.equal(env.internals.formatters.size, 2, 'a different locale is a different shape');

    // Out-of-range digit inputs normalize onto their own clamped shapes.
    paint({ locale: 'en-US', minimumFractionDigits: -3, maximumFractionDigits: 99 });
    assert.equal(env.internals.formatters.size, 3, '-3/99 clamps to min 0 / max 15 — a new shape');
    paint({ locale: 'en-US', minimumFractionDigits: 4, maximumFractionDigits: 1 });
    assert.equal(env.internals.formatters.size, 4, 'max < min normalizes to min 4 / max 4 — another new shape');
    paint({ locale: 'en-US', minimumFractionDigits: 4, maximumFractionDigits: 4 });
    assert.equal(env.internals.formatters.size, 4, 'the explicit 4/4 shape reuses the normalized one');
});

test('fraction digits clamp to 0..15 with max >= min, and an unknown-locale formatter falls back without throwing', () => {
    const env = load();
    const el = valueEl();

    env.animate(el, 1.5, 1.5, 0, { locale: 'en-US', minimumFractionDigits: -3, maximumFractionDigits: 99 });
    assert.equal(el.textContent, '1.5', 'min clamped to 0 keeps the natural value readable');

    env.animate(el, 3.7, 3.7, 0, { locale: 'en_US' }); // structurally invalid tag → Intl throws → fallback
    assert.equal(el.textContent, '4', 'fallback rounds without the formatter');

    env.animate(el, 0.25, 0.25, 0, { style: 'percent' });
    assert.equal(el.textContent, '25%');

    env.animate(el, 1000000, 1000000, 0, { useGrouping: false });
    assert.equal(el.textContent, '1000000', 'grouping opt-out is honored');
    env.animate(el, 1000000, 1000000, 0, {});
    assert.equal(el.textContent, '1,000,000', 'grouping is on by default');
});

test('the cache is bounded: more than 64 distinct shapes clear it instead of growing forever', () => {
    const env = load();
    const el = valueEl();

    // 70 distinct (min, max) shape keys within the 0..15 clamp.
    let max = 0;
    for (let i = 0; i < 70; i++) {
        const min = i % 16;
        if (min > max) max = min;
        env.animate(el, 1, 1, 0, { locale: 'en-US', minimumFractionDigits: min, maximumFractionDigits: max });
        if (i % 16 === 15) max++;
    }

    assert.ok(env.internals.formatters.size < 70, `cache was cleared and rebuilt (size ${env.internals.formatters.size})`);
    assert.ok(env.internals.formatters.size > 0, 'the clear is followed by repopulation, not a permanent empty cache');

    el.textContent = '';
    env.animate(el, 2.5, 2.5, 0, { locale: 'en-US', maximumFractionDigits: 2 });
    assert.equal(el.textContent, '2.5', 'a cleared cache still formats correctly afterwards');
});

test('cancel is idempotent, safe for a never-animated element, and stops the loop before its next paint', () => {
    const env = load();
    const el = valueEl();

    env.cancel(null);
    env.cancel({});
    env.cancel(el); // never animated
    assert.deepEqual(env.cancelled, []);

    env.animate(el, 0, 100, 1000, {});
    assert.equal(env.pendingCount(), 1);
    env.cancel(el);
    assert.equal(env.cancelled.length, 1, 'cancelAnimationFrame was asked to stop the pending frame');

    const paints = el.paints;
    env.pump(500);
    assert.equal(el.paints, paints, 'the cancelled loop painted nothing');

    env.cancel(el); // second cancel is a no-op, not a throw
    assert.equal(env.cancelled.length, 1, 'the second cancel finds no live state and issues no cancel call');
});

test('reduced motion paints the target immediately and schedules no frame', () => {
    const env = load({ matchMedia: () => ({ matches: true }) });
    const el = valueEl();

    env.animate(el, 0, 42, 1000, {});
    assert.equal(el.textContent, '42');
    assert.equal(env.pendingCount(), 0);
});

test('prefersReducedMotion is false without matchMedia and survives a throwing matchMedia', () => {
    assert.equal(load().prefersReducedMotion(), false, 'no matchMedia host means no preference');
    assert.equal(load({ matchMedia: () => { throw new Error('torn-down host'); } }).prefersReducedMotion(),
        false, 'a throwing matchMedia still reports no preference');
});

test('a host without a frame clock paints the target as the only honest frame', () => {
    const env = load({ raf: false });
    const el = valueEl();

    env.animate(el, 0, 7, 1000, {});
    assert.equal(el.textContent, '7');
    assert.equal(env.pendingCount(), 0);
});

test('zero or invalid input never starts a loop: duration 0 paints, invalid target is a no-op', () => {
    const env = load();
    const el = valueEl();

    env.animate(el, 0, 9, 0, {});
    assert.equal(el.textContent, '9', 'zero duration is an immediate paint');
    assert.equal(env.pendingCount(), 0);

    const paints = el.paints;
    env.animate(el, 0, Number.NaN, 1000, {});
    env.animate(el, 0, Number.POSITIVE_INFINITY, 1000, {});
    env.animate(el, 0, '9', 1000, {});
    env.animate(null, 0, 9, 1000, {});
    assert.equal(el.paints, paints, 'invalid targets and a missing element paint nothing');
    assert.equal(env.pendingCount(), 0);
});

test('a detached element stops its own loop instead of repainting a node that left the document', () => {
    const env = load();
    const el = valueEl();

    env.animate(el, 0, 100, 1000, {});
    env.pump(0);
    el.isConnected = false;
    env.pump(500);
    assert.equal(env.pendingCount(), 0, 'no further frame is scheduled for a removed element');
});

test('cancelAll stops every live ticker found under .tm-animated-number__value', () => {
    const el1 = valueEl();
    const el2 = valueEl();
    const env = load({ document: { querySelectorAll: () => [el1, el2] } });

    env.animate(el1, 0, 10, 1000, {});
    env.animate(el2, 0, 20, 1000, {});
    env.cancelAll();

    const paints = el1.paints + el2.paints;
    env.pump(500);
    assert.equal(el1.paints + el2.paints, paints, 'both loops stopped');

    assert.doesNotThrow(() => load().cancelAll(), 'cancelAll without a document is a no-op');
});
