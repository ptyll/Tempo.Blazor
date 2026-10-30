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
// ── linkScrollGroup (phase 11): the shared portfolio axis + every card's timeline in one group ──

function group() {
    const a = scroller(2000, 500);
    const b = scroller(1000, 500);
    const c = scroller(4000, 500);
    return { a, b, c };
}

test('linkScrollGroup fans one scroll out to every member as a ratio of the scrollable distance', () => {
    const gantt = load();
    const { a, b, c } = group();
    assert.equal(gantt.linkScrollGroup('group-1', [a, b, c]), true);

    a.scrollLeft = 375; // a quarter of a's distance (1500)
    a.fire('scroll');
    assert.equal(b.scrollLeft, 125, 'a quarter of b (500)');
    assert.equal(c.scrollLeft, 875, 'a quarter of c (3500)');

    c.scrollLeft = 1750; // half of c
    c.fire('scroll');
    assert.equal(a.scrollLeft, 750);
    assert.equal(b.scrollLeft, 250);
});

test('linkScrollGroup swallows the echo events of its own programmatic writes', () => {
    const gantt = load();
    const { a, b } = group();
    gantt.linkScrollGroup('group-2', [a, b]);

    a.scrollLeft = 375; // a quarter of a's distance (1500)
    a.fire('scroll');
    const written = b.scrollLeft;
    b.fire('scroll'); // the browser reports b's programmatic write back
    assert.equal(a.scrollLeft, 375, 'the echo does not drive a back');
    assert.equal(b.scrollLeft, written);
});

test('linkScrollGroup is idempotent for the same member set and rebuilds on a changed set', () => {
    const gantt = load();
    const { a, b, c } = group();
    let adds = 0;
    const add = a.addEventListener;
    a.addEventListener = (type, fn, opts) => { adds++; add(type, fn, opts); };

    assert.equal(gantt.linkScrollGroup('group-3', [a, b]), true);
    assert.equal(gantt.linkScrollGroup('group-3', [b, a]), true, 'the same set in another order');
    assert.equal(adds, 1, 'no second listener pair');

    assert.equal(gantt.linkScrollGroup('group-3', [a, b, c]), true);
    assert.equal(c.has('scroll'), true, 'the changed set is rebuilt with the new member');
    assert.equal(adds, 2, 'a got one new listener for the rebuilt group');
});

test('linkScrollGroup with fewer than two elements unlinks and reports false', () => {
    const gantt = load();
    const { a, b } = group();
    assert.equal(gantt.linkScrollGroup('group-4', [a]), false);
    assert.equal(gantt.linkScrollGroup('group-4', []), false);
    assert.equal(gantt.linkScrollGroup('group-4', null), false);
    assert.equal(a.has('scroll'), false);

    gantt.linkScrollGroup('group-4', [a, b]);
    gantt.linkScrollGroup('group-5', [a]); // a second id over the same element unlinks group-4's entry on a
    assert.equal(b.has('scroll'), true);
});

test('unlinkScrollGroup removes every listener and is idempotent', () => {
    const gantt = load();
    const { a, b, c } = group();
    gantt.linkScrollGroup('group-6', [a, b, c]);

    gantt.unlinkScrollGroup('group-6');
    [a, b, c].forEach(el => assert.equal(el.has('scroll'), false));
    gantt.unlinkScrollGroup('group-6'); // idempotent
});

test('different group ids never share listeners', () => {
    const gantt = load();
    const { a, b, c } = group();
    gantt.linkScrollGroup('left', [a, b]);
    gantt.linkScrollGroup('right', [b, c]);

    a.scrollLeft = 1000; // two thirds of a's distance (1500)
    a.fire('scroll');
    assert.equal(b.scrollLeft, 1000 / 3, 'b follows a through the left group');
    assert.equal(c.scrollLeft, 0, 'c is untouched by the left group');
});

// ── Members of different clientWidth (phase 11 review): the sticky header's scroller is wider than
// a card's timeline, and the mapping must stay pixel-true whenever the content widths match. ──

function sharedWidthGroup() {
    return {
        axis: scroller(2000, 800),    // the header's scroller: max 1200
        card: scroller(2000, 500),    // a card timeline: max 1500
        narrow: scroller(2000, 1200), // a wide-viewport card: max 800
    };
}

test('equal scroll widths map pixel-for-pixel, clamped to each member range', () => {
    const gantt = load();
    const { axis, card, narrow } = sharedWidthGroup();
    gantt.linkScrollGroup('px-1', [axis, card, narrow]);

    axis.scrollLeft = 600;
    axis.fire('scroll');
    assert.equal(card.scrollLeft, 600, 'pixels, not a ratio (the ratio would give 750)');
    assert.equal(narrow.scrollLeft, 600);

    axis.scrollLeft = 1200; // the axis's own end
    axis.fire('scroll');
    assert.equal(card.scrollLeft, 1200);
    assert.equal(narrow.scrollLeft, 800, 'clamped to the narrower member, never overscrolled');
});

test('different scroll widths still fall back to the ratio', () => {
    const gantt = load();
    const header = scroller(2000, 500);
    const card = scroller(4000, 500); // twice the content: the pixel map cannot reach the end
    gantt.linkScrollGroup('px-2', [header, card]);

    header.scrollLeft = 750; // half of the header's distance
    header.fire('scroll');
    assert.equal(card.scrollLeft, 1750, 'half of the card distance (3500)');
});

test('a clamped no-op write leaves no echo marker, so a later real scroll is not swallowed', () => {
    const gantt = load();
    const { axis, narrow } = sharedWidthGroup();
    gantt.linkScrollGroup('px-3', [axis, narrow]);

    axis.scrollLeft = 1000;
    axis.fire('scroll');
    assert.equal(narrow.scrollLeft, 800, 'clamped');
    narrow.fire('scroll'); // the browser reports the write back; the echo is swallowed
    assert.equal(axis.scrollLeft, 1000);

    axis.scrollLeft = 1200;
    axis.fire('scroll');
    assert.equal(narrow.scrollLeft, 800, 'still clamped — this write changed nothing');

    narrow.scrollLeft = 400; // the user scrolls the clamped member
    narrow.fire('scroll');
    assert.equal(axis.scrollLeft, 400);

    narrow.scrollLeft = 800; // …and back to exactly the clamped position
    narrow.fire('scroll');
    assert.equal(axis.scrollLeft, 800, 'the stale marker must not swallow the user scroll');
});

test('the initial equalization marks its writes so its echo does not fan back out', () => {
    const gantt = load();
    const a = scroller(2000, 500);
    const b = scroller(2000, 500);
    a.scrollLeft = 500;
    gantt.linkScrollGroup('init-1', [a, b]);

    assert.equal(b.scrollLeft, 500, 'equalized to the first member');
    b.fire('scroll'); // the browser reports the initial write back
    assert.equal(a.scrollLeft, 500, 'the echo of the initial write does not drive the leader');

    a.scrollLeft = 300;
    a.fire('scroll');
    assert.equal(b.scrollLeft, 300);

    a.scrollLeft = 500; // the user scrolls the leader back to the equalized position
    a.fire('scroll');
    assert.equal(b.scrollLeft, 500, 'no stale marker from the initial echo may swallow this');
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