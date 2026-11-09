// tm-otp-input.js tests with hand-rolled DOM stubs (same approach as tm-focus-trap.test.mjs):
// no jsdom; the stubs implement only what the module touches. Covers the DOM-side contract the
// bUnit tests cannot reach: beforeinput filtering, inserted-char (not last-char) semantics,
// multi-char distribute, IME composition, local navigation and the first-empty-cell redirect.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attach, detach, setCellValue, setCellValues, focusCell, selectCell, readPastedText, restartShake } from '../tm-otp-input.js';

function makeCell(root, index) {
    const listeners = new Map();
    return {
        root,
        index,
        value: '',
        disabled: false,
        readOnly: false,
        focusCalls: 0,
        selectCalls: 0,
        getAttribute(name) {
            return name === 'data-tm-otp-index' ? String(index) : null;
        },
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        dispatch(type, event = {}) {
            event.currentTarget ??= this;
            event.target ??= this;
            listeners.get(type)?.(event);
        },
        focus() {
            this.focusCalls++;
            root.activeCell = this;
            // The real DOM re-targets a focusin at the root when focus moves.
            root.dispatch('focusin', { target: this });
        },
        select() { this.selectCalls++; },
    };
}

function makeRoot(length, kind = 'numeric') {
    const listeners = new Map();
    const root = {
        dataset: { tmOtpKind: kind },
        activeCell: null,
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        dispatch(type, event = {}) { listeners.get(type)?.(event); },
        querySelectorAll() { return root.cells; },
    };
    root.cells = Array.from({ length }, (_, i) => makeCell(root, i));
    return root;
}

function dotnetStub() {
    const calls = [];
    return {
        calls,
        invokeMethodAsync(method, ...args) {
            calls.push({ method, args });
            return Promise.resolve();
        },
    };
}

function attachStub(root, dotNet, overrides = {}) {
    attach(root, dotNet, {
        inputMethod: 'OnCellInputAsync',
        distributeMethod: 'DistributeFromAsync',
        ...overrides,
    });
}

function beforeinput(cell, { data = null, inputType = 'insertText', isComposing = false }) {
    const event = {
        data, inputType, isComposing,
        defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
    };
    cell.dispatch('beforeinput', event);
    return event;
}

function paste(root, cell, text) {
    const event = {
        target: cell,
        clipboardData: { getData: (type) => (type === 'text/plain' ? text : '') },
        defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
    };
    root.dispatch('paste', event);
    return event;
}

test('numeric beforeinput prevents letters and reports nothing', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = beforeinput(root.cells[0], { data: 'a' });
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.cells[0].value, '');
    assert.equal(dotNet.calls.length, 0);
});

test('numeric beforeinput accepts a digit, reports it and auto-advances', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = beforeinput(root.cells[0], { data: '5' });
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.cells[0].value, '5');
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, '5'] }]);
    assert.equal(root.activeCell, root.cells[1]);
});

test('alphanumeric uppercases the inserted character', () => {
    const root = makeRoot(6, 'alphanumeric');
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    beforeinput(root.cells[0], { data: 'a' });
    assert.equal(root.cells[0].value, 'A');
    assert.deepEqual(dotNet.calls.at(-1), { method: 'OnCellInputAsync', args: [0, 'A'] });
});

test('mixed input keeps only accepted characters', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = beforeinput(root.cells[0], { data: 'x7-' });
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.cells[0].value, '7');
});

test('inserted character wins on a filled cell (caret-before insert)', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[0].value = '7';
    beforeinput(root.cells[0], { data: '3' });
    assert.equal(root.cells[0].value, '3');
    assert.deepEqual(dotNet.calls.at(-1), { method: 'OnCellInputAsync', args: [0, '3'] });
});

test('multi-character insert preventDefaults and distributes from the target cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = beforeinput(root.cells[1], { data: '73', inputType: 'insertFromPaste' });
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.cells[1].value, '7');
    assert.equal(root.cells[2].value, '3');
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [1, '73'] }]);
});

test('typing into a later cell fills the first empty cell instead', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    beforeinput(root.cells[3], { data: '9' });
    assert.equal(root.cells[0].value, '9');
    assert.equal(root.cells[3].value, '');
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, '9'] }]);
    assert.equal(root.activeCell, root.cells[1]);
});

test('pointer-driven focusin on a later cell redirects to the first empty cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.dispatch('pointerdown', { target: root.cells[3] });
    root.cells[3].focus();
    assert.equal(root.activeCell, root.cells[0]);
    // Filling the gap stops the redirect: the next empty cell is the focus target.
    root.cells[0].value = '1';
    root.dispatch('pointerdown', { target: root.cells[3] });
    root.cells[3].focus();
    assert.equal(root.activeCell, root.cells[1]);
    // Keyboard focus is never redirected.
    root.cells[4].focus();
    assert.equal(root.activeCell, root.cells[4]);
});

test('delete input reports the cleared cell value', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[0].value = '7';
    root.cells[0].dispatch('input', { inputType: 'deleteContentBackward' });
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, '7'] }]);
});

test('backspace in an empty cell clears the previous one and focuses it', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[0].value = '7';
    const event = { key: 'Backspace', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    root.cells[1].dispatch('keydown', event);
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.cells[0].value, '');
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, ''] }]);
    assert.equal(root.activeCell, root.cells[0]);
});

test('backspace in a filled cell stays with the browser default', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[0].value = '7';
    const event = { key: 'Backspace', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    root.cells[0].dispatch('keydown', event);
    assert.equal(event.defaultPrevented, false);
    assert.equal(dotNet.calls.length, 0);
    assert.equal(root.activeCell, null);
});

test('backspace on the first cell is a no-op', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = { key: 'Backspace', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    root.cells[0].dispatch('keydown', event);
    assert.equal(event.defaultPrevented, false);
    assert.equal(dotNet.calls.length, 0);
});

test('arrow keys, Home and End move focus locally without circuit calls', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const key = (cell, name) => cell.dispatch('keydown', {
        key: name,
        defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
    });
    key(root.cells[2], 'ArrowRight');
    assert.equal(root.activeCell, root.cells[3]);
    key(root.cells[3], 'ArrowLeft');
    assert.equal(root.activeCell, root.cells[2]);
    key(root.cells[2], 'Home');
    assert.equal(root.activeCell, root.cells[0]);
    key(root.cells[0], 'End');
    assert.equal(root.activeCell, root.cells[5]);
    key(root.cells[0], 'ArrowLeft');
    assert.equal(root.activeCell, root.cells[5]);
    assert.equal(dotNet.calls.length, 0);
});

test('isComposing beforeinput passes through and compositionend commits once', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);

    root.cells[0].dispatch('compositionstart');
    const during = beforeinput(root.cells[0], { data: 'n', inputType: 'insertCompositionText', isComposing: true });
    assert.equal(during.defaultPrevented, false);
    root.cells[0].dispatch('input', { inputType: 'insertCompositionText' });
    assert.equal(dotNet.calls.length, 0);

    root.cells[0].value = 'ん';
    root.cells[0].dispatch('compositionend', { data: '5' });
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, '5'] }]);
    assert.equal(root.activeCell, root.cells[1]);
});

test('compositionend with no accepted character clears the cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[0].value = 'x';
    root.cells[0].dispatch('compositionend', { data: 'x' });
    assert.equal(root.cells[0].value, '');
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, ''] }]);
});

test('setCellValue pushes a corrected value without events', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    setCellValue(root, 2, 'A');
    assert.equal(root.cells[2].value, 'A');
    assert.equal(dotNet.calls.length, 0);
});

test('focusCell and selectCell respect disabled cells', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[1].disabled = true;
    focusCell(root, 1);
    selectCell(root, 1);
    assert.equal(root.cells[1].focusCalls, 0);
    focusCell(root, 0);
    selectCell(root, 0);
    assert.equal(root.cells[0].focusCalls, 2);
    assert.equal(root.cells[0].selectCalls, 1);
});

test('detach removes every listener', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    detach(root);
    beforeinput(root.cells[0], { data: '5' });
    root.cells[1].dispatch('keydown', { key: 'Backspace' });
    root.cells[0].dispatch('compositionend', { data: '5' });
    assert.equal(dotNet.calls.length, 0);
    assert.equal(root.cells[0].value, '');
});

test('reattach replaces the previous session without double reporting', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    attachStub(root, dotnetStub()); // second session, other ref
    beforeinput(root.cells[0], { data: '5' });
    assert.equal(dotNet.calls.length, 0); // first ref must be gone
    assert.equal(root.cells[0].value, '5');
});

// ── Phase 2: paste, autofill, readOnly ───────────────────────────────────

test('readPastedText reads text/plain from the clipboard event', () => {
    const event = { clipboardData: { getData: (t) => (t === 'text/plain' ? '1 2-3' : 'x') } };
    assert.equal(readPastedText(event), '1 2-3');
    assert.equal(readPastedText({}), '');
});

test('paste into a cell distributes the code from that cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = paste(root, root.cells[2], '3456');
    assert.equal(event.defaultPrevented, true);
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [2, '3456'] }]);
    assert.deepEqual(root.cells.map((c) => c.value), ['', '', '3', '4', '5', '6']);
});

test('paste cleans spaces and dashes', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    paste(root, root.cells[0], '12 34-5');
    assert.deepEqual(root.cells.map((c) => c.value), ['1', '2', '3', '4', '5', '']);
});

test('paste longer than the remaining cells is truncated', () => {
    const root = makeRoot(4);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    paste(root, root.cells[2], '56789');
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [2, '56'] }]);
    assert.deepEqual(root.cells.map((c) => c.value), ['', '', '5', '6']);
});

test('paste with no accepted characters reports nothing', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = paste(root, root.cells[0], '----');
    assert.equal(event.defaultPrevented, true);
    assert.equal(dotNet.calls.length, 0);
});

test('paste into a readOnly or disabled cell is left to the browser no-op', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[1].readOnly = true;
    let event = paste(root, root.cells[1], '3456');
    assert.equal(event.defaultPrevented, false);
    root.cells[1].readOnly = false;
    root.cells[1].disabled = true;
    event = paste(root, root.cells[1], '3456');
    assert.equal(event.defaultPrevented, false);
    assert.equal(dotNet.calls.length, 0);
});

test('autofill input event with a multi-character value distributes the whole code', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[0].value = '123456';
    root.cells[0].dispatch('input', { inputType: 'insertText' });
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [0, '123456'] }]);
    assert.deepEqual(root.cells.map((c) => c.value), ['1', '2', '3', '4', '5', '6']);
});

test('autofill input event starting at a later cell distributes from that cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[2].value = '3456';
    root.cells[2].dispatch('input', { inputType: 'insertText' });
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [2, '3456'] }]);
    assert.deepEqual(root.cells.map((c) => c.value), ['', '', '3', '4', '5', '6']);
});

test('single-character autofill (no beforeinput) is applied at the first empty cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[3].value = '9';
    root.cells[3].dispatch('input', { inputType: 'insertText' });
    assert.equal(root.cells[0].value, '9');
    assert.equal(root.cells[3].value, '');
    assert.deepEqual(dotNet.calls, [{ method: 'OnCellInputAsync', args: [0, '9'] }]);
});

test('readOnly cells ignore beforeinput, autofill and backspace-step-back', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    root.cells[1].readOnly = true;

    const event = beforeinput(root.cells[1], { data: '5' });
    assert.equal(event.defaultPrevented, false);

    root.cells[1].value = '12';
    root.cells[1].dispatch('input', { inputType: 'insertText' });
    assert.equal(dotNet.calls.length, 0);

    const backspace = { key: 'Backspace', defaultPrevented: false, preventDefault() { this.defaultPrevented = true; } };
    root.cells[1].dispatch('keydown', backspace);
    assert.equal(backspace.defaultPrevented, false);
    assert.equal(dotNet.calls.length, 0);
});

test('insertReplacementText autofill reads text/plain from dataTransfer and distributes once', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = {
        data: null,
        inputType: 'insertReplacementText',
        dataTransfer: { getData: (t) => (t === 'text/plain' ? '123456' : '') },
        defaultPrevented: false,
        preventDefault() { this.defaultPrevented = true; },
    };
    root.cells[0].dispatch('beforeinput', event);
    assert.equal(event.defaultPrevented, true);
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [0, '123456'] }]);
    assert.deepEqual(root.cells.map((c) => c.value), ['1', '2', '3', '4', '5', '6']);
});

test('insertFromDrop with a null payload is left to the browser (no preventDefault)', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = beforeinput(root.cells[0], { data: null, inputType: 'insertFromDrop' });
    assert.equal(event.defaultPrevented, false);
    assert.equal(dotNet.calls.length, 0);
});

test('paste prefers the last accepted run with exactly the remaining length', () => {
    const root = makeRoot(6, 'alphanumeric');
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    paste(root, root.cells[0], 'Your code: AB12CD');
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [0, 'AB12CD'] }]);
    assert.deepEqual(root.cells.map((c) => c.value), ['A', 'B', '1', '2', 'C', 'D']);
});

test('paste heuristic falls back to truncation when no run matches exactly', () => {
    const root = makeRoot(6, 'alphanumeric');
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    paste(root, root.cells[0], 'Code AB12CD extra');
    // Runs: "Code"(4), "AB12CD"(6), "extra"(5) — "AB12CD" still matches the remaining 6.
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [0, 'AB12CD'] }]);
});

test('setCellValues writes the whole model array in one batch without events', () => {
    const root = makeRoot(4);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    setCellValues(root, ['1', '', '3', '']);
    assert.deepEqual(root.cells.map((c) => c.value), ['1', '', '3', '']);
    setCellValues(root, ['', '', '', '']);
    assert.deepEqual(root.cells.map((c) => c.value), ['', '', '', '']);
    assert.equal(dotNet.calls.length, 0);
});

test('restartShake removes and re-adds the invalid class around a reflow', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const log = [];
    root.classList = {
        remove: (cls) => log.push(['remove', cls]),
        add: (cls) => log.push(['add', cls]),
    };
    root.offsetWidth = 0; // stub: reading it is the reflow
    restartShake(root);
    assert.deepEqual(log, [
        ['remove', 'tm-otp-input--invalid'],
        ['add', 'tm-otp-input--invalid'],
    ]);
    // Null-safe on a root without a classList.
    delete root.classList;
    restartShake(root);
});
