// tmGantt.linkScroll: the docked workload panel and the timeline share one date axis, so their horizontal scroll
// is mapped as a ratio of the scrollable distance, without echoing the programmatic write back at its source.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import vm from 'node:vm';

const source = readFileSync(fileURLToPath(new URL('../gantt.js', import.meta.url)), 'utf8');

function load() {
    const window = {};
    vm.runInNewContext(source, { window });
    return window.tmGantt;
}

function scroller(scrollWidth, clientWidth) {
    const handlers = new Map();
    return {
        scrollWidth, clientWidth, scrollLeft: 0, _writes: 0,
        addEventListener(type, fn) { handlers.set(type, fn); },
        removeEventListener(type, fn) { if (handlers.get(type) === fn) handlers.delete(type); },
        has(type) { return handlers.has(type); },
        fire(type) { handlers.get(type)?.(); },
        closest() { return null; },
        querySelector() { return null; },
    };
}

function pair() {
    const timeline = scroller(2000, 500);
    const workload = scroller(1000, 500);
    const root = { querySelector: () => timeline };
    workload.closest = () => root;
    return { timeline, workload };
}

test('linkScroll maps the position as a ratio of the scrollable distance, both ways', () => {
    const gantt = load();
    const { timeline, workload } = pair();
    gantt.linkScroll(workload);

    workload.scrollLeft = 250; // half of 500
    workload.fire('scroll');
    assert.equal(timeline.scrollLeft, 750, 'half of the timeline distance (1500)');

    timeline.scrollLeft = 1500; // the end
    timeline.fire('scroll');
    assert.equal(workload.scrollLeft, 500, 'the end of the workload distance');
});

test('the echo scroll event of a programmatic write does not drive the source back', () => {
    const gantt = load();
    const { timeline, workload } = pair();
    gantt.linkScroll(workload);

    workload.scrollLeft = 100;
    workload.fire('scroll');
    const written = timeline.scrollLeft;
    workload.scrollLeft = 100; // nothing moved the workload since
    timeline.fire('scroll'); // the browser reports the timeline write as a scroll event
    assert.equal(workload.scrollLeft, 100, 'unchanged: the echo is swallowed');
    assert.equal(timeline.scrollLeft, written);
});

test('linkScroll without a Gantt around the element, or without an element, does nothing', () => {
    const gantt = load();
    gantt.linkScroll(null);
    const alone = scroller(1000, 500);
    gantt.linkScroll(alone);
    assert.equal(alone.has('scroll'), false);
});

test('unlinkScroll removes both listeners, and relinking never doubles them', () => {
    const gantt = load();
    const { timeline, workload } = pair();
    gantt.linkScroll(workload);
    gantt.linkScroll(workload);
    assert.equal(workload.has('scroll'), true);

    gantt.unlinkScroll(workload);
    assert.equal(workload.has('scroll'), false);
    assert.equal(timeline.has('scroll'), false);
    gantt.unlinkScroll(workload); // idempotent
});
test('linkScroll reports whether a link exists: true with a timeline, false without', () => {
    const gantt = load();
    const { workload } = pair();
    assert.equal(gantt.linkScroll(workload), true);

    assert.equal(gantt.linkScroll(null), false);
    assert.equal(gantt.linkScroll(scroller(1000, 500)), false, 'no Gantt around the element');

    const orphan = scroller(1000, 500);
    orphan.closest = () => ({ querySelector: () => null });
    assert.equal(gantt.linkScroll(orphan), false, 'a Gantt without a timeline');
});

test('linkScroll is idempotent for the same timeline: no second listener pair, the link survives', () => {
    const gantt = load();
    const { timeline, workload } = pair();
    let adds = 0;
    const add = timeline.addEventListener;
    timeline.addEventListener = (type, fn, opts) => { adds++; add(type, fn, opts); };

    assert.equal(gantt.linkScroll(workload), true);
    const link = workload.__tmGanttLink;
    assert.equal(gantt.linkScroll(workload), true);
    assert.equal(adds, 1, 'the second call adds nothing');
    assert.equal(workload.__tmGanttLink, link, 'the very same link is kept');

    workload.scrollLeft = 250;
    workload.fire('scroll');
    assert.equal(timeline.scrollLeft, 750, 'still linked');
});

test('linkScroll relinks when the timeline element was replaced, and unlinks the old one', () => {
    const gantt = load();
    const { timeline, workload } = pair();
    const root = { current: timeline, querySelector() { return this.current; } };
    workload.closest = () => root;

    assert.equal(gantt.linkScroll(workload), true);
    assert.equal(timeline.has('scroll'), true);

    const replacement = scroller(3000, 500);
    root.current = replacement;
    assert.equal(gantt.linkScroll(workload), true);
    assert.equal(timeline.has('scroll'), false, 'the stale timeline lost its listener');
    assert.equal(replacement.has('scroll'), true);
    assert.equal(workload.__tmGanttLink.timeline, replacement);

    workload.scrollLeft = 250;
    workload.fire('scroll');
    assert.equal(replacement.scrollLeft, 1250, 'half of the new distance (2500)');
});

test('linkScroll drops a stale link when the timeline is gone and reports false', () => {
    const gantt = load();
    const { timeline, workload } = pair();
    const root = { current: timeline, querySelector() { return this.current; } };
    workload.closest = () => root;
    gantt.linkScroll(workload);

    root.current = null;
    assert.equal(gantt.linkScroll(workload), false);
    assert.equal(timeline.has('scroll'), false);
    assert.equal(workload.has('scroll'), false);
});
// History drawer: focus returns to the button that opened it.
function focusable(name, connected = true) {
    return { name, isConnected: connected, focused: 0, focus() { this.focused++; } };
}

function drawerWith(document) {
    const drawer = { contains: () => false, ownerDocument: document };
    return drawer;
}

test('noteOpener remembers the focused element outside the drawer, restoreOpener focuses it once', () => {
    const document = { activeElement: null, body: { name: 'body' } };
    const window = {};
    vm.runInNewContext(source, { window, document });
    const gantt = window.tmGantt;
    const button = focusable('history-btn');
    const drawer = drawerWith(document);
    document.activeElement = button;

    gantt.noteOpener(drawer);
    assert.equal(gantt.restoreOpener(drawer), true);
    assert.equal(button.focused, 1);
    assert.equal(gantt.restoreOpener(drawer), false, 'the note is consumed');
});

test('noteOpener ignores body and elements inside the drawer; restoreOpener skips a detached opener', () => {
    const document = { activeElement: null, body: { name: 'body' } };
    const window = {};
    vm.runInNewContext(source, { window, document });
    const gantt = window.tmGantt;
    const drawer = drawerWith(document);

    document.activeElement = document.body;
    gantt.noteOpener(drawer);
    assert.equal(gantt.restoreOpener(drawer), false);

    const inside = focusable('inside');
    drawer.contains = (el) => el === inside;
    document.activeElement = inside;
    gantt.noteOpener(drawer);
    assert.equal(gantt.restoreOpener(drawer), false);

    const gone = focusable('gone', false);
    document.activeElement = gone;
    drawer.contains = () => false;
    gantt.noteOpener(drawer);
    assert.equal(gantt.restoreOpener(drawer), false);
    assert.equal(gone.focused, 0);
    gantt.noteOpener(null);
    assert.equal(gantt.restoreOpener(null), false);
});