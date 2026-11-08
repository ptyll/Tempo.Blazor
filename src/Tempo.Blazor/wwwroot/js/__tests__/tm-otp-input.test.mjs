// tm-otp-input.js tests with hand-rolled DOM stubs (same approach as tm-focus-trap.test.mjs):
// no jsdom; the stubs implement only what the module touches. Covers the DOM-side contract the
// bUnit tests cannot reach: beforeinput filtering, inserted-char (not last-char) semantics,
// multi-char distribute, IME composition, local navigation and the first-empty-cell redirect.
import test from 'node:test';
import assert from 'node:assert/strict';
import { attach, detach, setCellValue, focusCell, selectCell } from '../tm-otp-input.js';

function makeCell(root, index) {
    const listeners = new Map();
    return {
        root,
        index,
        value: '',
        disabled: false,
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

test('multi-character insert preventDefaults and calls DistributeFrom at the first empty cell', () => {
    const root = makeRoot(6);
    const dotNet = dotnetStub();
    attachStub(root, dotNet);
    const event = beforeinput(root.cells[1], { data: '73', inputType: 'insertFromPaste' });
    assert.equal(event.defaultPrevented, true);
    assert.equal(root.cells[1].value, '');
    assert.deepEqual(dotNet.calls, [{ method: 'DistributeFromAsync', args: [0, '73'] }]);
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
