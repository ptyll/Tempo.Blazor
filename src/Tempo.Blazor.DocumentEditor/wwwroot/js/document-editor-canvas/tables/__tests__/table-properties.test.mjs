// Phase 7 (command-layer plan): the properties side panel routes setTableProperties /
// setCellProperties — composite commands the engine never registered (it only had the
// granular settablecellformat/borders/margins operations), so the panel was a no-op.
import assert from 'node:assert/strict';
import test from 'node:test';
import { createCanvasCommandRuntime } from '../../commands/dispatcher.mjs';
import { createHistoryStore } from '../../history/history-store.mjs';

function paragraph(id, text) {
    return {
        id,
        type: 'paragraph',
        order: 1,
        paragraphProperties: {},
        content: {
            type: 'paragraph',
            runs: [{ id: `${id}-run`, type: 'text', text, marks: [] }],
        },
    };
}

function createModel() {
    return {
        documentId: 'phase-7-table-properties',
        version: 0,
        body: {
            blocks: [{
                id: 'props-table',
                sectionId: 'section-1',
                type: 'table',
                order: 10,
                content: {
                    type: 'table',
                    table: {
                        layout: { cellPadding: 8 },
                        rows: [1, 2].map(rowNumber => ({
                            id: `props-table-row-${rowNumber}`,
                            cells: [1, 2].map(cellNumber => ({
                                id: `props-table-r${rowNumber}c${cellNumber}`,
                                columnSpan: 1,
                                rowSpan: 1,
                                isHeader: false,
                                merge: { isOrigin: true, originCellId: null },
                                width: 200,
                                backgroundColor: null,
                                borders: {},
                                verticalAlignment: 0,
                                padding: 8,
                                blocks: [paragraph(`props-table-r${rowNumber}c${cellNumber}-p`, `cell ${rowNumber}${cellNumber}`)],
                                preserve: {},
                            })),
                        })),
                    },
                },
            }],
        },
        sections: [{ id: 'section-1', blocks: [] }],
    };
}

function createRuntime(initialModel) {
    const state = {
        model: initialModel,
        selection: { anchor: { blockId: 'props-table-r1c1-p', offset: 0 }, focus: { blockId: 'props-table-r1c1-p', offset: 0 } },
    };
    const runtime = createCanvasCommandRuntime({
        getModel: () => state.model,
        getSelection: () => state.selection,
        history: createHistoryStore(),
        commit(change) {
            state.model = change.model;
            state.selection = change.selection ?? state.selection;
        },
    });
    return { runtime, state };
}

const table = state => state.model.body.blocks.find(block => block.id === 'props-table').content.table;
const cell = (state, rowNumber, cellNumber) => table(state).rows[rowNumber - 1].cells[cellNumber - 1];

test('setTableProperties applies width, alignment, padding, background and borders in one command', () => {
    const { runtime, state } = createRuntime(createModel());

    const result = runtime.execCommand('setTableProperties', {
        cellId: 'props-table-r1c1',
        width: 520,
        alignment: 'Center',
        cellPadding: 12,
        backgroundColor: '#eef2ff',
        borders: { top: '2px solid #1e3a8a', bottom: '2px solid #1e3a8a' },
    });

    assert.equal(result.handled, true, 'setTableProperties must be a registered command');
    assert.equal(result.result?.changed, true);
    const layout = table(state).layout;
    assert.equal(layout.width, 520);
    assert.equal(String(layout.alignment).toLowerCase(), 'center');
    assert.equal(layout.cellPadding, 12);
    assert.equal(layout.backgroundColor, '#eef2ff');
    assert.equal(layout.borders?.top, '2px solid #1e3a8a');
    assert.equal(layout.borders?.bottom, '2px solid #1e3a8a');
});

test('setTableProperties partial payload leaves omitted properties unchanged', () => {
    const { runtime, state } = createRuntime(createModel());
    runtime.execCommand('setTableProperties', { cellId: 'props-table-r1c1', width: 480, alignment: 'Right' });

    const result = runtime.execCommand('setTableProperties', { cellId: 'props-table-r1c1', backgroundColor: '#fef9c3' });

    assert.equal(result.result?.changed, true);
    const layout = table(state).layout;
    assert.equal(layout.width, 480, 'omitted width must stay');
    assert.equal(String(layout.alignment).toLowerCase(), 'right', 'omitted alignment must stay');
    assert.equal(layout.backgroundColor, '#fef9c3');
});

test('setTableProperties is one atomic undo step', () => {
    const { runtime, state } = createRuntime(createModel());
    const before = JSON.stringify(state.model.body.blocks);

    runtime.execCommand('setTableProperties', {
        cellId: 'props-table-r1c1',
        width: 520,
        alignment: 'Center',
        cellPadding: 12,
        backgroundColor: '#eef2ff',
    });

    const undone = runtime.execCommand('undo');
    assert.equal(undone.handled, true);
    assert.equal(JSON.stringify(state.model.body.blocks), before, 'a single undo must revert ALL applied table properties');
});

test('setCellProperties applies width (whole column), background, vertical alignment, padding and borders', () => {
    const { runtime, state } = createRuntime(createModel());

    const result = runtime.execCommand('setCellProperties', {
        cellId: 'props-table-r1c2',
        width: 260,
        backgroundColor: '#dcfce7',
        verticalAlignment: 'Middle',
        padding: 14,
        borders: { left: '1px dashed #16a34a' },
    });

    assert.equal(result.handled, true, 'setCellProperties must be a registered command');
    assert.equal(result.result?.changed, true);
    const target = cell(state, 1, 2);
    assert.equal(target.backgroundColor, '#dcfce7');
    assert.equal(target.verticalAlignment, 1, 'Middle must map to the numeric vertical alignment');
    assert.equal(target.padding, 14);
    assert.equal(target.borders?.left, '1px dashed #16a34a');
    // Width applies to the whole column (same contract as the resize drag).
    assert.equal(cell(state, 1, 2).width, 260);
    assert.equal(cell(state, 2, 2).width, 260);
    // The other column stays untouched.
    assert.equal(cell(state, 1, 1).width, 200);
    assert.equal(cell(state, 1, 1).backgroundColor, null);
});

test('setCellProperties partial payload and single-step undo', () => {
    const { runtime, state } = createRuntime(createModel());
    const before = JSON.stringify(state.model.body.blocks);

    runtime.execCommand('setCellProperties', { cellId: 'props-table-r1c1', backgroundColor: '#fee2e2', padding: 4 });
    const target = cell(state, 1, 1);
    assert.equal(target.backgroundColor, '#fee2e2');
    assert.equal(target.padding, 4);
    assert.equal(target.width, 200, 'omitted width must stay');
    assert.equal(target.verticalAlignment, 0, 'omitted vertical alignment must stay');

    const undone = runtime.execCommand('undo');
    assert.equal(undone.handled, true);
    assert.equal(JSON.stringify(state.model.body.blocks), before, 'a single undo must revert all applied cell properties');
});

// The panel edits a cell the caret may NOT be in (the engine reports an empty / first-block selection after a blur): the command must put the
// caret back INSIDE the cell it edited, otherwise the host sees "selection left the table" and the table inspector disappears.
for (const [command, payload] of [
    ['setTableProperties', { cellId: 'props-table-r2c2', backgroundColor: '#fef9c3' }],
    ['setCellProperties', { cellId: 'props-table-r2c2', backgroundColor: '#dcfce7' }],
]) {
    test(`${command} puts the caret into the edited cell when the incoming selection is outside the table`, () => {
        const model = createModel();
        model.body.blocks.unshift(paragraph('before-table', 'Heading'));
        const { runtime, state } = createRuntime(model);
        state.selection = { anchor: { blockId: 'before-table', offset: 0 }, focus: { blockId: 'before-table', offset: 0 } };

        const result = runtime.execCommand(command, payload);

        assert.equal(result.result?.changed, true);
        assert.equal(state.selection.focus.blockId, 'props-table-r2c2-p', 'the caret is in the first block of the edited cell');
        assert.equal(state.selection.anchor.blockId, 'props-table-r2c2-p');
    });
}
