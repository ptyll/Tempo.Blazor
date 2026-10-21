// data-table.js pinned-offset measurement (phase 10, review round 1).
// The real data-table.js runs against a hand-rolled DOM. Two left-pinned columns and two body rows
// must each receive their OWN column offset (0, then the first header's width) — the old loop walked
// header and body cells together, so every body cell landed at the sum of the header widths. A
// right-pinned column stacks from the end. Offsets are written once on the wrapper, keyed by the
// header's cell index, because a Blazor re-render throws away an inline style set on the cell.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));
const dataTableUrl = pathToFileURL(path.resolve(here, '../data-table.js')).href;

function el(tag, classes, rect, children = []) {
    const set = new Set(classes);
    const node = {
        tagName: tag.toUpperCase(),
        classList: { contains: name => set.has(name) },
        rect,
        children,
        style: {
            props: new Map(),
            setProperty(name, value) { node.style.props.set(name, value); },
        },
        getBoundingClientRect: () => node.rect,
    };
    return node;
}

function wrapperWith(headerCells, bodyRows) {
    const headerRow = el('tr', [], { width: 0, height: 0 }, headerCells);
    const thead = el('thead', [], { width: 0, height: 0 }, [headerRow]);
    const tbody = el('tbody', [], { width: 0, height: 0 }, bodyRows);
    const table = el('table', ['tm-data-table'], { width: 0, height: 0 }, [thead, tbody]);
    const root = el('div', ['tm-data-table-wrapper'], { width: 600, height: 200 }, [table]);
    root.querySelector = selector => selector === 'thead tr' ? headerRow : null;
    root.querySelectorAll = () => [];
    return root;
}

let tmDataTable;

async function boot() {
    globalThis.document = { currentScript: { src: dataTableUrl }, readyState: 'complete' };
    globalThis.window = {};
    await import(`../data-table.js?${Math.random()}`);
    tmDataTable = globalThis.window.tmDataTable;
}

test.beforeEach(boot);
test.afterEach(() => {
    delete globalThis.document;
    delete globalThis.window;
});

test('two left-pinned columns offset each row by its own column, not the header sum', () => {
    const name = el('th', ['tm-col-pinned', 'tm-col-pinned-left'], { width: 120, height: 40 });
    const role = el('th', ['tm-col-pinned', 'tm-col-pinned-left'], { width: 80, height: 40 });
    const age = el('th', [], { width: 60, height: 40 });
    const row = () => el('tr', [], { width: 0, height: 0 }, [
        el('td', ['tm-col-pinned', 'tm-col-pinned-left'], { width: 120, height: 32 }),
        el('td', ['tm-col-pinned', 'tm-col-pinned-left'], { width: 80, height: 32 }),
        el('td', [], { width: 60, height: 32 }),
    ]);
    const root = wrapperWith([name, role, age], [row(), row()]);

    recompute(root);

    assert.equal(root.style.props.get('--tm-pin-left-0'), '0px', 'the first pinned column sticks at the edge');
    assert.equal(root.style.props.get('--tm-pin-left-1'), '120px', 'the second stacks after the first header, not after every cell');
    assert.equal(root.style.props.has('--tm-pin-left-2'), false, 'an unpinned column gets no offset');
    // Body cells are never written: a re-render would discard it, and the old bug put the sum there.
    for (const body of root.children[0].children[1].children) {
        for (const cell of body.children) {
            assert.equal(cell.style.props.size, 0, 'a body cell keeps the offset the component renders, not one the script paints');
        }
    }
});

test('right-pinned columns stack from the end and body cells stay untouched', () => {
    const name = el('th', [], { width: 100, height: 40 });
    const role = el('th', ['tm-col-pinned', 'tm-col-pinned-right'], { width: 70, height: 40 });
    const actions = el('th', ['tm-col-pinned', 'tm-col-pinned-right'], { width: 40, height: 40 });
    const row = el('tr', [], { width: 0, height: 0 }, [
        el('td', [], { width: 100, height: 32 }),
        el('td', ['tm-col-pinned', 'tm-col-pinned-right'], { width: 70, height: 32 }),
        el('td', ['tm-col-pinned', 'tm-col-pinned-right'], { width: 40, height: 32 }),
    ]);
    const root = wrapperWith([name, role, actions], [row]);

    recompute(root);

    assert.equal(root.style.props.get('--tm-pin-right-2'), '0px', 'the last right-pinned column sticks at the end');
    assert.equal(root.style.props.get('--tm-pin-right-1'), '40px', 'the one before it stacks by the last header width');
    assert.equal(row.children[1].style.props.size, 0, 'the right-pinned body cell is not painted by the script');
});

// The measurement is not exported (the component only observes). Reach it the way the observer
// does: install a ResizeObserver that fires immediately, which schedules the debounced recompute.
function recompute(root) {
    const scheduled = [];
    globalThis.ResizeObserver = class {
        constructor(callback) { this.callback = callback; }
        observe() { this.callback(); }
        disconnect() {}
    };
    globalThis.setTimeout = fn => { scheduled.push(fn); return scheduled.length; };
    globalThis.clearTimeout = () => {};
    tmDataTable.observePinnedOffsets(root);
    for (const fn of scheduled) fn();
}
