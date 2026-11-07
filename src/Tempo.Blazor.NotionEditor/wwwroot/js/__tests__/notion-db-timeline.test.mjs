// notion-db-timeline.js: the gesture is the shared pointer-drag helper (injected, no second
// implementation), a move writes left without calling Blazor, and pointerup commits once.
import test from 'node:test';
import assert from 'node:assert/strict';

const sessions = [];
globalThis.__tmNotionTimelineImport = async () => ({
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

const { attachTimeline } = await import('../notion-db-timeline.js');

function bar(recordId) {
    const style = { left: '10px', width: '40px' };
    const attrs = new Map([['data-record-id', recordId], ['data-timeline-bar', '']]);
    return {
        get style() { return style; },
        dataset: {},
        getAttribute: name => attrs.get(name) ?? null,
        hasAttribute: name => attrs.has(name),
        closest: selector => selector.includes('data-timeline-bar') ? bar.self : null,
        get self() { return this; },
    };
}

function rootOf() {
    return {
        dataset: { pxPerDay: '10' },
        classList: { added: [], add(name) { this.added.push(name); }, remove() {} },
    };
}

test('uses the shared helper and commits once on pointerup, not per move', async () => {
    sessions.length = 0;
    const commits = [];
    const root = rootOf();
    await attachTimeline(root, {
        invokeMethodAsync(name, recordId, days, resize) {
            commits.push({ name, recordId, days, resize });
            return Promise.resolve();
        },
    });

    const drag = sessions.find(s => s.kind === 'drag');
    assert.equal(drag.options.longPressMs, 500);
    const item = bar('rec-1');
    item.closest = selector => selector.includes('data-timeline-resize') ? null : item;

    drag.options.onStart({ target: item, dx: 0 });
    drag.options.onMove({ target: item, dx: 25 });
    drag.options.onMove({ target: item, dx: 30 });
    assert.equal(commits.length, 0, 'a pointermove must not call back into Blazor');
    assert.equal(item.style.left, `${10 + 30}px`);

    drag.options.onEnd({ target: item, dx: 30 });
    assert.equal(commits.length, 1);
    assert.equal(commits[0].name, 'OnBarCommitted');
    assert.equal(commits[0].days, 3);
    assert.equal(commits[0].resize, false);
});

test('a cancelled drag restores the bar and commits nothing', async () => {
    sessions.length = 0;
    const commits = [];
    await attachTimeline(rootOf(), {
        invokeMethodAsync(...args) { commits.push(args); return Promise.resolve(); },
    });
    const drag = sessions.find(s => s.kind === 'drag');
    const item = bar('rec-2');
    item.closest = selector => selector.includes('data-timeline-resize') ? null : item;
    drag.options.onStart({ target: item });
    drag.options.onMove({ target: item, dx: 20 });
    drag.options.onCancel({ target: item });
    assert.equal(item.style.left, '10px');
    assert.equal(commits.length, 0);
});

test('keyboard step commits through the same callback', async () => {
    sessions.length = 0;
    const commits = [];
    await attachTimeline(rootOf(), {
        invokeMethodAsync(name, recordId, days, resize) {
            commits.push({ days, resize });
            return Promise.resolve();
        },
    });
    const keys = sessions.find(s => s.kind === 'keys');
    assert.equal(keys.options.shift, true, 'Alt+Shift+Arrow is the resize chord the helper must accept');
    const item = bar('rec-3');
    item.closest = selector => selector.includes('data-timeline-resize') ? null : item;
    keys.options.onStep({ x: -1, shift: false, event: { target: { closest: () => item } } });
    assert.deepEqual(commits, [{ days: -1, resize: false }]);
});

test('a press on the resize handle resizes, and the mode survives a retargeted move', async () => {
    sessions.length = 0;
    const commits = [];
    await attachTimeline(rootOf(), {
        invokeMethodAsync(name, recordId, days, resize) {
            commits.push({ days, resize });
            return Promise.resolve();
        },
    });
    const drag = sessions.find(s => s.kind === 'drag');
    const item = bar('rec-4');
    item.closest = selector => selector.includes('data-timeline-resize') ? null : item;
    const handle = {
        matches: selector => selector === '[data-timeline-resize]',
        closest: selector => selector.includes('data-timeline-bar') ? item : null,
    };

    drag.options.onStart({ target: handle, dx: 0 });
    assert.equal(item.dataset.dragMode, 'resize');
    drag.options.onMove({ target: item, dx: 20 });
    assert.equal(item.style.width, `${40 + 20}px`);
    assert.equal(item.style.left, '10px');
    drag.options.onEnd({ target: item, dx: 20 });
    assert.equal(item.style.width, '40px', 'the live width is restored before the commit callback');
    assert.equal(item.style.left, '10px');
    assert.deepEqual(commits, [{ days: 2, resize: true }]);
});

test('Alt+Shift+Arrow resizes through the same callback', async () => {
    sessions.length = 0;
    const commits = [];
    await attachTimeline(rootOf(), {
        invokeMethodAsync(name, recordId, days, resize) {
            commits.push({ days, resize });
            return Promise.resolve();
        },
    });
    const keys = sessions.find(s => s.kind === 'keys');
    const item = bar('rec-5');
    keys.options.onStep({ x: 1, shift: true, event: { target: { closest: () => item } } });
    assert.deepEqual(commits, [{ days: 1, resize: true }]);
});
