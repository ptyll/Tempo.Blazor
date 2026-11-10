// tm-scheduled-runs.js tests — the Alt+arrow chord of TmScheduledRuns.
//
// The component owns the MOVE (which window a key reaches, the refusal, the single announcement); the module
// owns only what Blazor cannot express: the chord is also the browser's Back/Forward shortcut, and the
// focused element inside a run row is the move menu's trigger whose content fences Blazor's key dispatch.
// These tests pin the decision that matters: exactly the four Alt+arrow keys on a row that declares them
// resolve to that row's run, every other chord and every other target resolves to nothing, and a missing
// root attaches nothing.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attachMoveKeys, MOVE_KEYS, resolveMoveStep, ROW_SELECTOR } from '../tm-scheduled-runs.js';

/** A stand-in for a row that declares the chord and carries its run id. */
function fakeRow(runId) {
    const row = {
        attributes: new Map([['data-run-id', runId]]),
        getAttribute(name) {
            return this.attributes.get(name) ?? null;
        },
    };
    row.closest = selector => (selector === ROW_SELECTOR ? row : null);
    return row;
}

/** A keyboard event whose target sits inside `row`. */
function keyEvent(key, row, modifiers = { altKey: true }) {
    return {
        key,
        target: { closest: selector => (selector === ROW_SELECTOR ? row : null) },
        altKey: false,
        ctrlKey: false,
        metaKey: false,
        shiftKey: false,
        ...modifiers,
    };
}

test('MOVE_KEYS: the chord owns exactly the four arrows', () => {
    assert.deepEqual([...MOVE_KEYS], ['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown']);
});

test('resolveMoveStep: every Alt+arrow resolves to the focused row\'s run', () => {
    const row = fakeRow('r1');

    for (const key of MOVE_KEYS) {
        assert.deepEqual(resolveMoveStep(keyEvent(key, row)), { runId: 'r1', key },
            `${key} belongs to the run the row shows`);
    }
});

test('resolveMoveStep: a key that is not the chord resolves to nothing', () => {
    const row = fakeRow('r1');

    assert.equal(resolveMoveStep(keyEvent('ArrowRight', row, { altKey: false })), null,
        'a plain arrow keeps scrolling the page');
    assert.equal(resolveMoveStep(keyEvent('ArrowRight', row, { altKey: true, ctrlKey: true })), null,
        'Ctrl+Alt is somebody else\'s shortcut');
    assert.equal(resolveMoveStep(keyEvent('ArrowRight', row, { altKey: true, shiftKey: true })), null,
        'Alt+Shift+Arrow is the resize convention, not the move chord');
    assert.equal(resolveMoveStep(keyEvent('PageDown', row)), null);
    assert.equal(resolveMoveStep(keyEvent('Enter', row, { altKey: false })), null);
    assert.equal(resolveMoveStep(null), null);
});

test('resolveMoveStep: an event outside a run row resolves to nothing', () => {
    const row = fakeRow('r1');

    assert.equal(resolveMoveStep(keyEvent('ArrowRight', null)), null,
        'the same chord on the day buttons or the page must not move a run');
    assert.equal(resolveMoveStep({ key: 'ArrowRight', altKey: true }), null, 'no target at all');
    assert.equal(resolveMoveStep(keyEvent('ArrowRight', { ...row, attributes: new Map() })), null,
        'a row without an id names no run');
});

test('attachMoveKeys: a missing root attaches nothing and never throws', async () => {
    assert.equal(await attachMoveKeys(null, null), null);
    assert.equal(await attachMoveKeys({}, null), null);
});
