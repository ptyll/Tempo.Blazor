// tm-inline-diff.js — overflow tab stops for TmInlineDiff Whole layout.
// Hand-rolled DOM stubs (same approach as tm-time-selector.test.mjs): no jsdom.
import { describe, test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const source = readFileSync(fileURLToPath(new URL('../tm-inline-diff.js', import.meta.url)), 'utf8');

function load({ raf = false } = {}) {
    const pending = new Map();
    let nextFrameId = 1;
    const sandbox = { console };
    if (raf) {
        sandbox.requestAnimationFrame = (fn) => { const id = nextFrameId++; pending.set(id, fn); return id; };
        sandbox.cancelAnimationFrame = (id) => { pending.delete(id); };
    }
    sandbox.globalThis = sandbox;
    sandbox.ResizeObserver = undefined;
    const probe = '\n;globalThis.__mod = { syncOverflow, release };';
    vm.runInNewContext(source.replace(/^export /gm, '') + probe, sandbox, { filename: 'tm-inline-diff.js' });
    return {
        ...sandbox.__mod,
        pump() {
            const batch = [...pending.values()];
            pending.clear();
            for (const fn of batch) fn();
        },
        pendingCount: () => pending.size,
    };
}

function box({ scrollHeight, clientHeight }) {
    const attrs = {};
    return {
        scrollHeight,
        clientHeight,
        children: [],
        getAttribute(name) { return Object.prototype.hasOwnProperty.call(attrs, name) ? attrs[name] : null; },
        setAttribute(name, value) { attrs[name] = String(value); },
        removeAttribute(name) { delete attrs[name]; },
        attrs,
    };
}

describe('tm-inline-diff overflow', { concurrency: false }, () => {

    test('sets tabindex only while a box actually overflows', () => {
        const env = load();
        const overflowing = box({ scrollHeight: 80, clientHeight: 40 });
        const short = box({ scrollHeight: 20, clientHeight: 40 });

        env.syncOverflow('one', overflowing, 'Proposed', short, 'Current');

        assert.equal(overflowing.getAttribute('tabindex'), '0');
        assert.equal(overflowing.getAttribute('role'), 'region');
        assert.equal(overflowing.getAttribute('aria-label'), 'Proposed');
        assert.equal(short.getAttribute('tabindex'), null);
        assert.equal(short.getAttribute('role'), null);
        assert.equal(short.getAttribute('aria-label'), null);
    });

    test('a 1px overflow is not a tab stop', () => {
        const env = load();
        const almost = box({ scrollHeight: 41, clientHeight: 40 });
        env.syncOverflow('tol', almost, 'Proposed', box({ scrollHeight: 10, clientHeight: 40 }), 'Current');
        assert.equal(almost.getAttribute('tabindex'), null, 'sub-pixel rounding must not mint a tab stop');
    });

    test('release strips overflow attributes and is idempotent', () => {
        const env = load();
        const overflowing = box({ scrollHeight: 80, clientHeight: 40 });
        env.syncOverflow('rel', overflowing, 'Proposed', box({ scrollHeight: 10, clientHeight: 40 }), 'Current');
        env.release('rel');
        assert.equal(overflowing.getAttribute('tabindex'), null);
        assert.equal(overflowing.getAttribute('role'), null);
        assert.equal(overflowing.getAttribute('aria-label'), null);
        env.release('rel');
        env.release('missing');
    });

    test('a second syncOverflow replaces the first session', () => {
        const env = load();
        const first = box({ scrollHeight: 80, clientHeight: 40 });
        const second = box({ scrollHeight: 90, clientHeight: 40 });
        env.syncOverflow('swap', first, 'Proposed', box({ scrollHeight: 10, clientHeight: 40 }), 'Current');
        env.syncOverflow('swap', second, 'Proposed', box({ scrollHeight: 10, clientHeight: 40 }), 'Current');
        assert.equal(first.getAttribute('tabindex'), null, 'the replaced session is released');
        assert.equal(second.getAttribute('tabindex'), '0');
    });
});
