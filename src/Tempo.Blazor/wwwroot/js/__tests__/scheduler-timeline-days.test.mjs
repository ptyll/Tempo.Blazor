// scheduler.js on the day-granularity timeline (Scale=Days): the horizontal scroll that brings the
// first event's day into view, and the touch-move commit that keeps the event's time of day and
// carries the day and the resource instead of a minute. The real scheduler.js runs against
// hand-rolled scrollers and elements; no DOM engine is needed for either path.
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const schedulerUrl = pathToFileURL(
    path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../scheduler.js')).href;

let scheduler;

async function boot() {
    globalThis.document = {
        readyState: 'complete',
        currentScript: { src: schedulerUrl },
        contains: () => true,
        elementFromPoint: () => null,
        addEventListener() {},
        removeEventListener() {},
    };
    globalThis.window = { TempoBlazor: undefined };
    globalThis.matchMedia = undefined;
    globalThis.requestAnimationFrame = () => 1;
    globalThis.cancelAnimationFrame = () => {};
    await import(`../scheduler.js?${Math.random()}`);
    scheduler = globalThis.window.TempoBlazor.Scheduler;
    await scheduler.ready;
}

test.beforeEach(boot);
test.afterEach(() => {
    scheduler?.destroy?.();
    delete globalThis.document;
    delete globalThis.window;
});

function classList(initial = []) {
    const set = new Set(initial);
    return {
        add: name => set.add(name),
        remove: name => set.delete(name),
        contains: name => set.has(name),
        names: () => [...set],
    };
}

/** A scroller with the two elements the scroll reads: the pinned label and the first chip's cell. */
function dayBoard({ labelWidth = 192, cellLeft = 600, chip = true } = {}) {
    const cell = { offsetLeft: cellLeft, dataset: { date: '2026-06-10', resource: 'web' } };
    const label = { offsetWidth: labelWidth };
    const chipElement = {
        closest: selector => (selector === '.tm-scheduler-timeline-day-cell' ? cell : null),
    };
    const scroller = {
        scrollLeft: 0,
        dataset: { tmScroll: 'timeline-days' },
        matches: selector => selector === '[data-tm-scroll]',
        querySelector(selector) {
            if (selector === '.tm-scheduler-timeline-day-event') return chip ? chipElement : null;
            if (selector === '.tm-scheduler-timeline-resource-header') return label;
            return null;
        },
    };
    return scroller;
}

test('scrollToTime on the day board brings the first event day past the pinned resource column', () => {
    const scroller = dayBoard({ labelWidth: 192, cellLeft: 600 });

    scheduler.scrollToTime(scroller, 8 * 60);

    assert.equal(scroller.scrollLeft, 408, 'the cell offset minus the pinned label width');
});

test('scrollToTime never scrolls left of the first column', () => {
    const scroller = dayBoard({ labelWidth: 192, cellLeft: 96 });

    scheduler.scrollToTime(scroller, 8 * 60);

    assert.equal(scroller.scrollLeft, 0);
});

test('a day board without events keeps the scroll it has', () => {
    const scroller = dayBoard({ chip: false });
    scroller.scrollLeft = 240;

    scheduler.scrollToTime(scroller, 8 * 60);

    assert.equal(scroller.scrollLeft, 240, 'nothing to reveal, so nothing moves');
});

test('the hour timeline still scrolls by the minute fraction', () => {
    const scroller = {
        scrollLeft: 0,
        scrollWidth: 1200,
        dataset: { tmScroll: 'timeline' },
        matches: () => false,
        querySelector: selector => (selector === '.tm-scheduler-timeline-resource-header' ? { offsetWidth: 200 } : null),
    };
    const root = { dataset: { rangeStart: '0', rangeMinutes: '1440' }, querySelector: () => scroller };

    scheduler.scrollToTime(root, 12 * 60);

    assert.equal(scroller.scrollLeft, 500, '(1200 - 200) * 0.5');
});

function daySession({ chipOverrides = {} } = {}) {
    const calls = [];
    const ref = { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
    const container = {
        dataset: { slotDuration: '30', date: '2026-06-10' },
        classList: classList(['tm-scheduler-timeline', 'tm-scheduler-timeline--days']),
    };
    const cell = {
        dataset: { date: '2026-06-11', resource: 'mail' },
        classList: classList(['tm-scheduler-timeline-day-cell']),
        getBoundingClientRect: () => ({ left: 400, top: 0, width: 140, height: 60 }),
        // A cell carries both the day and the resource it belongs to.
        closest: selector => (selector === '[data-date]' || selector === '[data-resource]' ? cell : null),
    };
    const chip = {
        dataset: { eventId: 'e1' },
        style: {},
        getAttribute: name => (name === 'draggable' ? 'true' : null),
        getBoundingClientRect: () => ({ left: 410, top: 0, width: 120, height: 40 }),
        classList: classList(['tm-scheduler-timeline-day-event']),
        closest: selector => {
            if (selector === '.tm-scheduler-timeline') return container;
            if (selector === '[data-date]') return cell;
            if (selector === '[data-resource]') return cell;
            if (selector === '.tm-scheduler') return null;
            return null;
        },
        ...chipOverrides,
    };
    scheduler.registerInstance(container, ref);
    scheduler._moveSession = {
        eventElement: chip,
        isMonth: false,
        isTimeline: false,
        isDays: true,
        container,
        column: cell,
        eventId: 'e1',
        dotNetRef: ref,
        slotMinutes: 30,
        rangeStart: 0,
        rangeMinutes: 1440,
        startDate: '2026-06-10',
        startResource: 'web',
        grabOffset: { x: 0, y: 0 },
        originOffset: 0,
        originalDraggable: true,
        targetDate: null,
        targetMinute: null,
        targetResource: null,
    };
    return { calls, chip, cell, container, ref };
}

test('a day-board drag commits the day and the resource, never a minute', async () => {
    const session = daySession();
    globalThis.document.elementFromPoint = () => ({
        closest: selector => {
            if (selector.includes('tm-scheduler-timeline-day-cell')) return session.cell;
            if (selector === '[data-date]') return session.cell;
            if (selector === '[data-resource]') return session.cell;
            return null;
        },
    });

    scheduler._moveDrag({ x: 430, y: 10, dx: 12, dy: 0 });
    assert.equal(scheduler._moveSession.targetMinute, -1, 'no minute axis: .NET keeps the time of day');
    assert.equal(scheduler._moveSession.targetDate, '2026-06-11');
    assert.equal(scheduler._moveSession.targetResource, 'mail');
    assert.equal(session.cell.classList.contains('tm-scheduler-timeline-slot--drop-target'), true,
        'the day cell is marked with the timeline drop class');
    assert.equal(session.chip.style.transform, 'translate(12px, 0px)', 'the preview follows the finger');

    scheduler._moveEnd();
    // The grabbed day travels with the drop (a continuation chip is not the event's first day).
    assert.deepEqual(session.calls[0],
        ['NotifyMoveComplete', 'e1', '2026-06-11', -1, 'mail', '2026-06-10']);
});

// ── The moving chip must get out of the finger's way ────────────────────────────────────────────

const schedulerCss = path.resolve(
    path.dirname(fileURLToPath(import.meta.url)),
    '../../css/components/_scheduler.css');

/** Every rule block whose selector list mentions this class. */
function cssBlocks(selectorClass) {
    const css = readFileSync(schedulerCss, 'utf8');
    const blocks = [];
    const pattern = /([^{}]+)\{([^}]*)\}/g;
    let match;
    while ((match = pattern.exec(css)) !== null) {
        if (match[1].includes(selectorClass)) blocks.push({ selector: match[1], body: match[2] });
    }
    return blocks;
}

test('the moving rules name the day chip in both selector lists', () => {
    const moving = cssBlocks('.tm-scheduler-event--moving');
    const compound = '.tm-scheduler-timeline-day-event.tm-scheduler-event--moving';

    const offsets = moving.filter(block => block.selector.includes(compound));
    assert.equal(offsets.length, 2,
        'the day chip is in both lists: the z-index/pointer-events one and the overflow one');
    assert.ok(moving.some(block => block.selector.includes(compound) && /pointer-events:\s*none/.test(block.body)),
        'the moving day chip carries pointer-events: none, so the finger reaches the cell underneath');
    assert.ok(moving.some(block => block.selector.includes(compound) && /overflow:\s*visible/.test(block.body)),
        'and it may paint outside its cell while it follows the finger');
});

test('an overflowing day board only shows the shadow on the edges that hide content', () => {
    assert.equal(scheduler._scrollShadowState(0, 900, 900), 'none', 'everything fits: no edge at all');
    assert.equal(scheduler._scrollShadowState(0, 1200, 900), 'end', 'at the start only the right edge hides content');
    assert.equal(scheduler._scrollShadowState(150, 1200, 900), 'both', 'scrolled to the middle: both edges');
    assert.equal(scheduler._scrollShadowState(300, 1200, 900), 'start', 'at the end only the pinned-column edge');
    assert.equal(scheduler._scrollShadowState(0, 901, 900), 'none', 'one pixel of slack is not an overflow');
});

// ── The day chip is lifted before it follows the finger ────────────────────────────────────────

/**
 * The moving chip only stops swallowing the hit-test because the stylesheet gives
 * `.tm-scheduler-timeline-day-event.tm-scheduler-event--moving` `pointer-events: none` (pinned by
 * the two tests above). This stub encodes exactly that one contract: the hit-test returns the chip
 * until it carries the moving class, and the cell the finger is really over afterwards.
 */
function hitTestFor(chip, originCell, targetCell, created) {
    globalThis.document.createElement = () => {
        created.push('element');
        return { className: '', textContent: '', style: {}, classList: classList() };
    };
    globalThis.document.elementFromPoint = () => (
        chip.classList.contains('tm-scheduler-event--moving') ? targetCell : chip);
    return originCell;
}

test('a lifted day chip lets the finger resolve the cell underneath, so the drop leaves the origin day', () => {
    const session = daySession();
    const originCell = session.cell;                       // the chip's own cell, 2026-06-11 / mail
    const targetCell = {
        dataset: { date: '2026-06-17', resource: 'web' },
        classList: classList(['tm-scheduler-timeline-day-cell']),
        getBoundingClientRect: () => ({ left: 700, top: 0, width: 140, height: 60 }),
        closest: selector => (
            selector.includes('tm-scheduler-timeline-day-cell')
            || selector === '[data-date]' || selector === '[data-resource]'
                ? targetCell
                : null),
    };
    const created = [];

    // The chip's own closest() walks up to its ORIGIN cell: that is why a non-lifted chip can never
    // leave its day — the finger is still over the chip, and the chip belongs to the origin cell.
    session.chip.closest = selector => {
        if (selector === '.tm-scheduler-timeline') return session.container;
        if (selector.includes('tm-scheduler-timeline-day-cell')) return originCell;
        if (selector === '[data-date]' || selector === '[data-resource]') return originCell;
        return null;
    };
    // The day board must never create the live time label, so the lookup finds nothing.
    session.chip.querySelector = () => null;
    hitTestFor(session.chip, originCell, targetCell, created);

    scheduler._moveStart({ x: 700, y: 20 });
    assert.equal(session.chip.classList.contains('tm-scheduler-event--moving'), true,
        'the moving class (and with it pointer-events: none) is on the chip');

    scheduler._moveDrag({ x: 700, y: 20, dx: 24, dy: 0 });
    assert.equal(scheduler._moveSession.targetDate, '2026-06-17', 'the finger resolves the day underneath');
    assert.equal(scheduler._moveSession.targetResource, 'web', 'and that cell\'s channel');

    scheduler._moveEnd();
    assert.deepEqual(session.calls[0],
        ['NotifyMoveComplete', 'e1', '2026-06-17', -1, 'web', '2026-06-10']);
    assert.equal(created.length, 0, 'a day board never paints a time label: it has no minute axis');
});

