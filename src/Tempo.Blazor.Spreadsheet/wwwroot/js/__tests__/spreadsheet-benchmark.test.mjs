// Node tests for the spreadsheet benchmark wheel/drag drivers. The shipped script is a classic
// `window.tmSpreadsheetBenchmark` IIFE, so the helpers live on `_internals` and run in a vm sandbox.
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const SOURCE = readFileSync(new URL('../spreadsheet-benchmark.js', import.meta.url), 'utf8');

function loadBenchmark() {
    const sandbox = {
        window: {},
        performance: { now: () => 0 },
        requestAnimationFrame: cb => { cb(0); return 0; },
        console,
        WheelEvent: class WheelEvent {
            constructor(type, init) {
                this.type = type;
                Object.assign(this, init);
            }
        },
        Event: class Event {
            constructor(type, init) {
                this.type = type;
                Object.assign(this, init);
            }
        },
        PointerEvent: class PointerEvent {
            constructor(type, init) {
                this.type = type;
                Object.assign(this, init);
            }
        },
        MouseEvent: class MouseEvent {
            constructor(type, init) {
                this.type = type;
                Object.assign(this, init);
            }
        },
    };
    vm.createContext(sandbox);
    vm.runInContext(SOURCE, sandbox, { filename: 'spreadsheet-benchmark.js' });
    const internals = sandbox.window.tmSpreadsheetBenchmark?._internals;
    assert.ok(internals, 'spreadsheet-benchmark.js must expose window.tmSpreadsheetBenchmark._internals');
    return internals;
}

test('resolveWheelEventCount_DefaultsToTwelveDeterministicEvents', () => {
    const internals = loadBenchmark();
    assert.equal(internals.resolveWheelEventCount({}), 12);
    assert.equal(internals.resolveWheelEventCount({ wheelDurationMs: 350 }), 12);
    assert.equal(internals.resolveWheelEventCount({ wheelEventCount: 8 }), 8);
});

test('measureWheelScroll_DispatchesExactlyTheConfiguredEventCount', async () => {
    const internals = loadBenchmark();
    const types = [];
    const grid = {
        classList: { contains: () => false },
        querySelector: () => null,
        scrollHeight: 4000,
        clientHeight: 400,
        scrollWidth: 2000,
        clientWidth: 400,
        scrollTop: 0,
        scrollLeft: 0,
        getBoundingClientRect: () => ({ left: 0, top: 0, right: 400, bottom: 400 }),
        focus() {},
        dispatchEvent(event) {
            types.push(event.type);
            return false;
        },
    };

    await internals.measureWheelScroll(grid, { wheelEventCount: 12 });
    const wheels = types.filter(type => type === 'wheel');
    assert.equal(wheels.length, 12, `expected 12 synthetic wheel events, got ${wheels.length}: ${types.join(',')}`);
});

test('classifyInteractionBudget_RequiresEightEventsAndHalfFrameRatio', () => {
    const internals = loadBenchmark();
    const miss = internals.classifyInteractionBudget(1, 1);
    assert.equal(miss.ok, false);
    assert.equal(miss.reason, 'precondition');

    const regression = internals.classifyInteractionBudget(12, 12);
    assert.equal(regression.ok, false);
    assert.equal(regression.reason, 'ratio');

    const ok = internals.classifyInteractionBudget(12, 5);
    assert.equal(ok.ok, true);
});
