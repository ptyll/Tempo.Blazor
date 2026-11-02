// notion-db-column-resize.js: one commit on pointerup, Alt+Arrow commits (flushed on keyup),
// width never below 60. The shared pointer-drag helper is injected so this file needs no browser.
import test from 'node:test';
import assert from 'node:assert/strict';

const sessions = [];
globalThis.__tmNotionResizeImport = async () => ({
    attach(root, options) {
        const session = { root, options, kind: 'drag' };
        sessions.push(session);
        return { detach() { session.detached = true; } };
    },
    attachKeyboardMove(root, options) {
        const session = { root, options, kind: 'keys' };
        sessions.push(session);
        return { detach() { session.detached = true; } };
    },
});

const { attachColumnResize } = await import('../notion-db-column-resize.js');

function handle(fieldId, width) {
    const attrs = new Map([
        ['data-resize-field', fieldId],
        ['aria-valuenow', String(width)],
    ]);
    const header = {
        style: {},
        dataset: { resizeStart: String(width) },
        getBoundingClientRect: () => ({ width }),
    };
    return {
        header,
        getAttribute: name => attrs.get(name) ?? null,
        setAttribute: (name, value) => attrs.set(name, String(value)),
        closest: () => header,
    };
}

function rootOf() {
    const listeners = new Map();
    return {
        classList: { add() {}, remove() {} },
        addEventListener(type, fn) { listeners.set(type, fn); },
        removeEventListener(type) { listeners.delete(type); },
        emit(type, event) { listeners.get(type)?.(event); },
    };
}

function dotNet(commits) {
    return {
        invokeMethodAsync(name, fieldId, width) {
            commits.push({ name, fieldId, width });
            return Promise.resolve();
        },
    };
}

test('pointerup commits once, not per move, and never below 60', async () => {
    sessions.length = 0;
    const commits = [];
    const root = rootOf();
    await attachColumnResize(root, dotNet(commits));
    const drag = sessions.find(s => s.kind === 'drag');
    assert.equal(drag.options.selector, '[data-resize-field]');

    const h = handle('field-1', 120);
    drag.options.onStart({ target: h });
    drag.options.onMove({ target: h, dx: 10 });
    drag.options.onMove({ target: h, dx: 20 });
    assert.equal(commits.length, 0, 'moves must not commit');

    drag.options.onEnd({ target: h, dx: 20 });
    assert.equal(commits.length, 1);
    assert.equal(commits[0].name, 'OnColumnResized');
    assert.equal(commits[0].width, 140);

    const narrow = handle('field-1', 80);
    drag.options.onStart({ target: narrow });
    drag.options.onEnd({ target: narrow, dx: -100 });
    assert.equal(commits.at(-1).width, 60);
});

test('Alt+Arrow schedules one commit, flushed on keyup', async () => {
    sessions.length = 0;
    const commits = [];
    const root = rootOf();
    const session = await attachColumnResize(root, dotNet(commits));
    const keys = sessions.find(s => s.kind === 'keys');
    const h = handle('field-2', 100);
    const event = { target: { closest: () => h }, altKey: true, key: 'ArrowRight' };

    keys.options.onStep({ x: 1, event });
    keys.options.onStep({ x: 1, event });
    assert.equal(commits.length, 0, 'repeat must not commit immediately');

    root.emit('keyup', { target: h });
    assert.equal(commits.length, 1);
    assert.equal(commits[0].fieldId, 'field-2');
    assert.ok(commits[0].width >= 60);
    assert.equal(commits[0].width, 132);

    session.detach();
});
