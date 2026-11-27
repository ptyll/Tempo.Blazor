import assert from 'node:assert/strict';
import test from 'node:test';
import { attachTabScroller, detachTabScroller, overflowState, scrollLeftForTab, syncTabScroller } from '../side-panel-tabs.mjs';

test('scrollLeftForTab: a tab already inside the scroller keeps the scroll', () => {
    assert.equal(scrollLeftForTab({ scrollLeft: 0, clientWidth: 300, tabStart: 20, tabEnd: 90 }), 0);
    assert.equal(scrollLeftForTab({ scrollLeft: 50, clientWidth: 300, tabStart: 120, tabEnd: 220 }), 50);
});

test('scrollLeftForTab: a tab past the end scrolls it in to the nearest edge, with a small margin', () => {
    const left = scrollLeftForTab({ scrollLeft: 0, clientWidth: 300, tabStart: 280, tabEnd: 380 });
    assert.equal(left, 380 - 300 + 8);
});

test('scrollLeftForTab: a tab before the start scrolls back to it', () => {
    assert.equal(scrollLeftForTab({ scrollLeft: 200, clientWidth: 300, tabStart: 60, tabEnd: 130 }), 60 - 8);
    assert.equal(scrollLeftForTab({ scrollLeft: 200, clientWidth: 300, tabStart: 2, tabEnd: 80 }), 0, 'never below zero');
});

test('scrollLeftForTab: a tab wider than the scroller aligns its start', () => {
    assert.equal(scrollLeftForTab({ scrollLeft: 0, clientWidth: 100, tabStart: 150, tabEnd: 400 }), 150 - 8);
});

test('overflowState reports the sides that still have tabs hidden', () => {
    assert.deepEqual(overflowState({ scrollLeft: 0, clientWidth: 300, scrollWidth: 300 }), { start: false, end: false });
    assert.deepEqual(overflowState({ scrollLeft: 0, clientWidth: 300, scrollWidth: 500 }), { start: false, end: true });
    assert.deepEqual(overflowState({ scrollLeft: 100, clientWidth: 300, scrollWidth: 500 }), { start: true, end: true });
    assert.deepEqual(overflowState({ scrollLeft: 200, clientWidth: 300, scrollWidth: 500 }), { start: true, end: false });
});

function fakeTablist({ clientWidth = 300, scrollWidth = 500, scrollLeft = 0, tabs }) {
    const attributes = new Map();
    const listeners = new Map();
    const list = {
        clientWidth,
        scrollWidth,
        scrollLeft,
        getBoundingClientRect: () => ({ left: 1000 }),
        querySelector: selector => (selector === '[role="tab"][aria-selected="true"]' ? tabs.find(tab => tab.selected) ?? null : null),
        setAttribute: (name, value) => attributes.set(name, String(value)),
        getAttribute: name => attributes.get(name) ?? null,
        addEventListener: (name, handler) => listeners.set(name, handler),
        removeEventListener: name => listeners.delete(name),
        listeners,
    };
    for (const tab of tabs) {
        tab.getBoundingClientRect = () => ({ left: 1000 + tab.start - list.scrollLeft, width: tab.end - tab.start });
    }

    return list;
}

test('syncTabScroller scrolls the active tab into view and publishes the overflow sides', () => {
    const list = fakeTablist({ tabs: [{ start: 0, end: 100 }, { start: 100, end: 200 }, { start: 400, end: 500, selected: true }] });

    syncTabScroller(list);

    assert.equal(list.scrollLeft, 500 - 300 + 8);
    assert.equal(list.getAttribute('data-overflow-start'), 'true');
    assert.equal(list.getAttribute('data-overflow-end'), 'false', '208 + 300 reaches the 500 px scroll width');
});

test('syncTabScroller leaves the scroll alone when the active tab is visible, and still publishes the cue', () => {
    const list = fakeTablist({ tabs: [{ start: 0, end: 100, selected: true }, { start: 400, end: 500 }] });

    syncTabScroller(list);

    assert.equal(list.scrollLeft, 0);
    assert.equal(list.getAttribute('data-overflow-start'), 'false');
    assert.equal(list.getAttribute('data-overflow-end'), 'true');
});

test('syncTabScroller tolerates no active tab and a missing element', () => {
    syncTabScroller(null);
    const list = fakeTablist({ tabs: [{ start: 0, end: 100 }] });
    syncTabScroller(list);
    assert.equal(list.scrollLeft, 0);
});

test('attach listens for scroll and keeps the cue in step; detach is idempotent', () => {
    const list = fakeTablist({ tabs: [{ start: 0, end: 100, selected: true }] });
    attachTabScroller(list);
    attachTabScroller(list);
    assert.equal(list.listeners.size, 1, 'a second attach does not stack listeners');

    list.scrollLeft = 120;
    list.listeners.get('scroll')();
    assert.equal(list.getAttribute('data-overflow-start'), 'true');

    detachTabScroller(list);
    detachTabScroller(list);
    assert.equal(list.listeners.size, 0);
});
