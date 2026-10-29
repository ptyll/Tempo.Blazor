// tmGantt.attachVirtualScroll: the session API contract. Two load modes:
//   - load(): no dynamic-import host, so the maths module cannot resolve — the session tears itself
//     down (the graceful no-module path) and only the registration/teardown answers are testable.
//   - loadWithVirtualModule(): vm importModuleDynamically serves the REAL gantt-virtual.mjs, so the
//     full session runs: the plain attachScrollSync listener, the window measurement, the
//     updateVirtualScroll enabled gate and remeasureVirtual. The ResizeObserver and the browser E2E
//     stay outside (GanttVirtualizationE2ETests).
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { spawnSync } from 'node:child_process';
import vm from 'node:vm';

// The full-session tests drive the dynamic import() inside gantt.js through a vm dynamic-import
// callback, which Node only enables under --experimental-vm-modules. Re-spawn THIS FILE with the
// flag when the shared runner (`node --test`) did not pass it, so `npm run test:overlay` needs no
// global runner change.
if (!process.execArgv.includes('--experimental-vm-modules') && process.env.TM_GANTT_VM_TEST !== '1') {
    const rerun = spawnSync(process.execPath, ['--experimental-vm-modules', fileURLToPath(import.meta.url)], {
        stdio: 'inherit',
        env: { ...process.env, TM_GANTT_VM_TEST: '1' },
    });
    process.exit(rerun.status ?? 1);
}

const source = readFileSync(fileURLToPath(new URL('../gantt.js', import.meta.url)), 'utf8');

function load() {
    const window = {};
    vm.runInNewContext(source, { window, console });
    return window.tmGantt;
}

async function loadWithVirtualModule() {
    const maths = await import('../gantt-virtual.mjs');
    const window = {};
    // URL is not a vm intrinsic: gantt.js resolves the module URL with `new URL(..., script.src)`.
    const document = { currentScript: { src: 'https://gantt.test/_content/Tempo.Blazor/js/gantt.js' } };
    vm.runInNewContext(source, { window, document, console, URL }, {
        importModuleDynamically: async (specifier) => {
            assert.ok(specifier.endsWith('gantt-virtual.mjs'), 'the session imports the maths module');
            return maths;
        },
    });
    return window.tmGantt;
}

function scroller(scrollTop, options = {}) {
    return {
        scrollTop,
        clientHeight: options.clientHeight ?? 800,
        scrollHeight: options.scrollHeight ?? 40000,
        listeners: {},
        addEventListener(type, fn) { this.listeners[type] = fn; },
        removeEventListener(type) { delete this.listeners[type]; },
        querySelector() { return null; },
    };
}

// ── The no-module path (the graceful fallback) ──────────────────────────────────────────────────

test('attachVirtualScroll without both bodies registers nothing and reports false', async () => {
    const gantt = load();
    assert.equal(await gantt.attachVirtualScroll('a', scroller(0), null, {}, 40, 10), false);
    assert.equal(await gantt.attachVirtualScroll('a', null, scroller(0), {}, 40, 10), false);
    assert.equal(await gantt.attachVirtualScroll(null, scroller(0), scroller(0), {}, 40, 10), false);
});

test('attachVirtualScroll is idempotent per id: a re-attach tears the previous session down first', async () => {
    const gantt = load();
    const tree = scroller(0);
    const timeline = scroller(0);
    // The maths module cannot load inside the vm (a bare dynamic import has no host), so the session
    // tears itself down — but the re-attach must never throw and never leave two sessions behind.
    await gantt.attachVirtualScroll('g', tree, timeline, { invokeMethodAsync() { } }, 40, 10);
    await gantt.attachVirtualScroll('g', tree, timeline, { invokeMethodAsync() { } }, 40, 10);
    await gantt.detachVirtualScroll('g'); // idempotent
    await gantt.detachVirtualScroll('g');
    assert.ok(true, 'no throw on any path');
});

test('scrollToRow without a session (or before the module resolved) answers false, never throws', async () => {
    const gantt = load();
    assert.equal(await gantt.scrollToRow('missing', 5, 'start'), false);
    assert.equal(await gantt.scrollToRow(null, 5, 'start'), false);
});

test('updateVirtualScroll without a session answers false', () => {
    const gantt = load();
    assert.equal(gantt.updateVirtualScroll('missing', 56, 10, true), false);
});

test('detachVirtualScroll of an unknown id is a no-op', async () => {
    const gantt = load();
    await gantt.detachVirtualScroll('never-attached');
    assert.ok(true);
});

test('remeasureVirtual without a session answers false', () => {
    const gantt = load();
    assert.equal(gantt.remeasureVirtual('missing'), false);
});

// ── The full session (the real maths module served through importModuleDynamically) ─────────────

async function attachSession(gantt, reports, tree, timeline, overscan = 10) {
    const dotNet = {
        invokeMethodAsync(method, ...args) {
            reports.push({ method, args });
            return Promise.resolve();
        },
    };
    assert.equal(await gantt.attachVirtualScroll('s', tree, timeline, dotNet, 40, overscan), true);
    // The module promise has resolved (and run the first measurement) by the next macrotask.
    await new Promise(resolve => setTimeout(resolve, 0));
}

test('the attached session syncs the peer scroller and reports the window with the visible row count', async () => {
    const gantt = await loadWithVirtualModule();
    const reports = [];
    const tree = scroller(0);
    const timeline = scroller(0);
    await attachSession(gantt, reports, tree, timeline);

    // The first measurement (scrollTop 0, 1000 rows from the 40000 px scroll height):
    assert.equal(reports.length, 1, 'the first measurement always reports');
    assert.equal(reports[0].method, 'OnViewportRowsChanged');
    assert.deepEqual(reports[0].args, [0, 30, 20], 'first 0, last 20 visible + 10 overscan, 800px / 40px = 20 visible rows');

    tree.scrollTop = 4000;
    tree.listeners.scroll({ currentTarget: tree });
    assert.equal(timeline.scrollTop, 4000, 'the plain scroll listener syncs the timeline body in the same tick');

    timeline.scrollTop = 1000;
    timeline.listeners.scroll({ currentTarget: timeline });
    assert.equal(tree.scrollTop, 1000, '…and the tree body follows the timeline body too');
    await new Promise(resolve => setTimeout(resolve, 0));

    assert.deepEqual(reports[1].args, [90, 130, 20], 'tree scroll: row 100 minus 10 overscan, ceil(4800/40)+10, 20 visible rows');
    assert.deepEqual(reports.at(-1).args, [15, 55, 20], 'timeline scroll: the sync moved the tree to row 25 and the window followed');
});

test('updateVirtualScroll(enabled=false) silences the reports; the flip back measures immediately', async () => {
    const gantt = await loadWithVirtualModule();
    const reports = [];
    const tree = scroller(0);
    const timeline = scroller(0);
    await attachSession(gantt, reports, tree, timeline);
    assert.equal(reports.length, 1);

    assert.equal(gantt.updateVirtualScroll('s', 40, 10, false), true);
    tree.scrollTop = 8000;
    tree.listeners.scroll({ currentTarget: tree });
    assert.equal(timeline.scrollTop, 8000, 'the sync keeps running while the reports are off');
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(reports.length, 1, 'no OnViewportRowsChanged while disabled');

    assert.equal(gantt.updateVirtualScroll('s', 40, 10, true), true);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(reports.length, 2, 'the flip to enabled clears the hysteresis and measures immediately');
    assert.deepEqual(reports[1].args, [190, 230, 20], 'the window follows the scrollTop the user is at (row 200)');
});

test('remeasureVirtual forces a fresh report (a .NET DrawVersion bump re-reads the row count)', async () => {
    const gantt = await loadWithVirtualModule();
    const reports = [];
    const tree = scroller(0, { scrollHeight: 40000 });
    const timeline = scroller(0);
    await attachSession(gantt, reports, tree, timeline);
    assert.equal(reports.length, 1);

    tree.scrollHeight = 20000; // the data shrank under the session (a collapse, a filter)
    assert.equal(gantt.remeasureVirtual('s'), true);
    await new Promise(resolve => setTimeout(resolve, 0));
    assert.equal(reports.length, 2, 'the forced measure bypasses the hysteresis');
    assert.deepEqual(reports[1].args, [0, 30, 20], '500 rows × 40 px now');
    assert.equal(gantt.remeasureVirtual('other'), false, 'no session, no answer');
});
