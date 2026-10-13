// tm-color-gradient.js on the shared pointer-drag helper (FZE-21, cluster-E 2.7). The real module
// and the real pointer-drag.js run against a hand-rolled DOM: fractions come from the element's
// bounding rect (never the event offset, which jumps on the thumb child), the preview streams
// through rAF-throttled OnGradientDrag calls, a release commits ONCE, and Escape / pointercancel /
// detach give OnGradientCancel and commit NOTHING. Arrows, Home and End on the three sliders are
// prevented here; Blazor performs the keyboard step.
import test from 'node:test';
import assert from 'node:assert/strict';
import { fileURLToPath, pathToFileURL } from 'node:url';
import path from 'node:path';

const here = path.dirname(fileURLToPath(import.meta.url));

function bag() {
    const map = new Map();
    return {
        addEventListener(type, fn) { map.set(type, [...(map.get(type) ?? []), fn]); },
        removeEventListener(type, fn) { map.set(type, (map.get(type) ?? []).filter(f => f !== fn)); },
        dispatch(type, event = {}) { for (const fn of [...(map.get(type) ?? [])]) fn(event); return event; },
        count(type) { return (map.get(type) ?? []).length; },
    };
}

let frames = [];
function installFrames() {
    frames = [];
    globalThis.requestAnimationFrame = (fn) => { frames.push(fn); return frames.length; };
    globalThis.cancelAnimationFrame = () => {};
}
function flushFrames() {
    const pending = frames;
    frames = [];
    for (const fn of pending) fn();
}

function el(classes = [], parent = null, rect = { left: 0, top: 0, width: 0, height: 0 }) {
    const set = new Set(classes);
    const node = {
        ...bag(),
        parent, dataset: {}, style: {}, attrs: new Map(), rect,
        captured: new Set(),
        classList: { add: n => set.add(n), remove: n => set.delete(n), contains: n => set.has(n) },
        setAttribute(name, value) { node.attrs.set(name, String(value)); },
        getBoundingClientRect: () => node.rect,
        setPointerCapture(id) { node.captured.add(id); },
        releasePointerCapture(id) { node.captured.delete(id); },
        contains: () => true,
    };
    // Pointer events bubble: a captured pointer's events fire on the part and reach the root.
    const own = node.dispatch;
    node.dispatch = (type, event = {}) => {
        own(type, event);
        (node.parent ?? doc)?.dispatch(type, event);
        return event;
    };
    return node;
}

/** The gradient: a 200x150 area and two 200x12 tracks, with a thumb child on each. */
function gradient({ alpha = true, areaRect, hueRect } = {}) {
    const root = el(['tm-color-gradient']);
    root.querySelector = (selector) => {
        const cls = selector.replace(/^\./, '');
        return [root.area, root.hue, root.alphaEl].find(p => p && p.classList.contains(cls)) ?? null;
    };
    root.area = el(['tm-color-gradient-area'], root, areaRect ?? { left: 0, top: 0, width: 200, height: 150 });
    const areaThumb = el(['tm-color-gradient-thumb'], root.area);
    root.hue = el(['tm-color-gradient-hue-track'], root, hueRect ?? { left: 0, top: 160, width: 200, height: 12 });
    const hueThumb = el(['tm-color-gradient-hue-thumb'], root.hue);
    if (alpha) {
        root.alphaEl = el(['tm-color-gradient-alpha-track'], root, { left: 0, top: 180, width: 200, height: 12 });
        const alphaThumb = el(['tm-color-gradient-alpha-thumb'], root.alphaEl);
        root.alphaEl.thumb = alphaThumb;
    }
    root.area.thumb = areaThumb;
    root.hue.thumb = hueThumb;
    return root;
}

function dotnet() {
    const calls = [];
    return { calls, invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } };
}

let doc;
let mod;

async function boot() {
    installFrames();
    doc = { ...bag(), readyState: 'complete', contains: () => true };
    globalThis.document = doc;
    globalThis.window = {};
    globalThis.matchMedia = undefined;
    mod = await import(`../tm-color-gradient.js?${Math.random()}`);
}

test.beforeEach(boot);
test.afterEach(() => {
    delete globalThis.document;
    delete globalThis.window;
    delete globalThis.requestAnimationFrame;
    delete globalThis.cancelAnimationFrame;
});

const ev = (target, over = {}) => ({
    target, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, isPrimary: true, clientX: 0, clientY: 0,
    preventDefault() { this.defaultPrevented = true; }, stopPropagation() {}, ...over,
});

// The commit lands directly, but a flush that chains promises lands a tick later.
const tick = () => new Promise(resolve => setImmediate(resolve));

test('fractions come from the bounding rect, not the offset: a press on the thumb child at the rect centre is 0.5', async () => {
    const root = gradient();
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    // The thumb sits at the centre; the event's target is the CHILD, the coordinates are the centre.
    root.area.thumb.dispatch('pointerdown', ev(root.area.thumb, { clientX: 100, clientY: 75 }));

    assert.deepEqual(ref.calls, [['OnGradientDrag', 'area', 0.5, 0.5]],
        'the fraction is measured against the area rect, so the thumb child cannot jump the value');
});

test('a drag streams OnGradientDrag through rAF and commits once on release', async () => {
    const root = gradient();
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    root.hue.dispatch('pointerdown', ev(root.hue, { clientX: 20, clientY: 166 }));
    root.hue.dispatch('pointermove', ev(root.hue, { clientX: 50, clientY: 166 }));
    root.hue.dispatch('pointermove', ev(root.hue, { clientX: 100, clientY: 166 }));
    flushFrames();
    root.hue.dispatch('pointerup', ev(root.hue, { clientX: 100, clientY: 166 }));
    await tick();

    // The press reports itself immediately; coalesced moves report the newest fraction per frame.
    assert.deepEqual(ref.calls.filter(c => c[0] === 'OnGradientDrag'), [
        ['OnGradientDrag', 'hue', 0.1, 0.5],
        ['OnGradientDrag', 'hue', 0.5, 0.5],
    ], 'the preview streams the newest fraction, coalescing the moves within a frame');
    assert.deepEqual(ref.calls.at(-1), ['OnGradientCommit', 'hue', 0.5, 0.5]);
    assert.equal(ref.calls.filter(c => c[0] === 'OnGradientCommit').length, 1, 'exactly one commit per gesture');
});

test('Escape mid-drag gives OnGradientCancel and no commit', async () => {
    const root = gradient();
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    root.area.dispatch('pointerdown', ev(root.area, { clientX: 20, clientY: 30 }));
    root.area.dispatch('pointermove', ev(root.area, { clientX: 60, clientY: 90 }));
    flushFrames();
    doc.dispatch('keydown', { key: 'Escape', stopPropagation() {} });

    // fy is the raw top-based fraction; C# converts it to brightness (1 - fy).
    assert.deepEqual(ref.calls, [['OnGradientDrag', 'area', 0.1, 0.2], ['OnGradientDrag', 'area', 0.3, 0.6], ['OnGradientCancel']],
        'the cancel is reported, nothing is committed');
    assert.equal(ref.calls.some(c => c[0] === 'OnGradientCommit'), false);
});

test('a tap (press and release without moving) commits once', async () => {
    const root = gradient();
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    root.alphaEl.dispatch('pointerdown', ev(root.alphaEl, { clientX: 50, clientY: 186 }));
    root.alphaEl.dispatch('pointerup', ev(root.alphaEl, { clientX: 50, clientY: 186 }));
    await tick();

    assert.deepEqual(ref.calls.filter(c => c[0] === 'OnGradientCommit'), [['OnGradientCommit', 'alpha', 0.25, 0.5]],
        'a tap sets the colour through the same commit');
});

test('pointercancel cancels instead of committing', async () => {
    const root = gradient();
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    root.hue.dispatch('pointerdown', ev(root.hue, { clientX: 0, clientY: 166 }));
    root.hue.dispatch('pointermove', ev(root.hue, { clientX: 80, clientY: 166 }));
    flushFrames();
    root.hue.dispatch('pointercancel', ev(root.hue, { clientX: 80, clientY: 166 }));

    assert.equal(ref.calls.some(c => c[0] === 'OnGradientCommit'), false, 'a cancelled gesture commits nothing');
    assert.deepEqual(ref.calls.at(-1), ['OnGradientCancel']);
});

test('detachGradient removes the listeners mid-gesture and cancels', async () => {
    const root = gradient();
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    root.hue.dispatch('pointerdown', ev(root.hue, { clientX: 0, clientY: 166 }));
    root.hue.dispatch('pointermove', ev(root.hue, { clientX: 80, clientY: 166 }));
    flushFrames();
    mod.detachGradient('g1');
    root.hue.dispatch('pointerup', ev(root.hue, { clientX: 120, clientY: 166 }));

    assert.equal(ref.calls.some(c => c[0] === 'OnGradientCommit'), false);
    assert.deepEqual(ref.calls.at(-1), ['OnGradientCancel'], 'the in-flight gesture was cancelled');
    root.hue.dispatch('pointerdown', ev(root.hue, { clientX: 10, clientY: 166 }));
    assert.equal(ref.calls.length, 3, 'detached: nothing new is reported');
});

test('a missing alpha track (ShowAlpha=false) attaches the rest and skips it', async () => {
    const root = gradient({ alpha: false });
    const ref = dotnet();
    mod.attachGradient(root, ref, 'g1');

    root.area.dispatch('pointerdown', ev(root.area, { clientX: 100, clientY: 75 }));
    root.area.dispatch('pointerup', ev(root.area, { clientX: 100, clientY: 75 }));
    await tick();

    assert.equal(ref.calls.some(c => c[1] === 'alpha'), false, 'no alpha gesture exists');
    assert.deepEqual(ref.calls.at(-1), ['OnGradientCommit', 'area', 0.5, 0.5]);
});

test('attaching again with the same id replaces the previous registration', async () => {
    const root = gradient();
    const ref1 = dotnet();
    const ref2 = dotnet();
    mod.attachGradient(root, ref1, 'g1');
    mod.attachGradient(root, ref2, 'g1');

    root.hue.dispatch('pointerdown', ev(root.hue, { clientX: 10, clientY: 166 }));
    root.hue.dispatch('pointerup', ev(root.hue, { clientX: 10, clientY: 166 }));
    await tick();

    assert.equal(ref1.calls.length, 0, 'the replaced registration is dead');
    assert.equal(ref2.calls.at(-1)[0], 'OnGradientCommit');
});

test('arrows, Home and End on a slider are prevented; Tab and other keys are not', async () => {
    const root = gradient();
    mod.attachGradient(root, dotnet(), 'g1');

    const prevented = (part, key) => {
        const e = { key, preventDefault() { e.defaultPrevented = true; } };
        part.dispatch('keydown', e);
        return e.defaultPrevented === true;
    };

    for (const [name, part] of [['area', root.area], ['hue', root.hue], ['alpha', root.alphaEl]]) {
        assert.equal(prevented(part, 'ArrowLeft'), true, `${name} ArrowLeft`);
        assert.equal(prevented(part, 'ArrowRight'), true);
        assert.equal(prevented(part, 'ArrowUp'), true);
        assert.equal(prevented(part, 'ArrowDown'), true);
        assert.equal(prevented(part, 'Home'), true);
        assert.equal(prevented(part, 'End'), true);
        assert.equal(prevented(part, 'Tab'), false, 'Tab keeps the browser behaviour');
        assert.equal(prevented(part, 'Enter'), false);
    }
});
