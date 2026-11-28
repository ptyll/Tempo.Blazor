import assert from 'node:assert/strict';
import test from 'node:test';
import { createCanvasSelectionController } from '../selection-controller.mjs';
import { createSelectionTestLayout } from './selection-test-helpers.mjs';

// The inspector of the host opens on a NEW or a DELIBERATE object selection only. The engine therefore tells every selection
// notification where an object selection came from: 'pointer' (a click on the object that did not drag), 'keyboard' (Tab-cycling)
// or 'layout' (everything else: a re-layout, a drag/resize/rotate preview, the state re-render). The stamp is ONE-SHOT.

function fakeElement(attributes = {}) {
    const listeners = new Map();
    const attrs = new Map(Object.entries(attributes));
    return {
        style: {},
        children: [],
        className: '',
        addEventListener(type, handler) { listeners.set(type, handler); },
        removeEventListener(type) { listeners.delete(type); },
        dispatch(type, event) { listeners.get(type)?.(event); },
        setAttribute(name, value) { attrs.set(name, String(value)); },
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        removeAttribute(name) { attrs.delete(name); },
        getBoundingClientRect() { return { left: 0, top: 0, width: 420, height: 720 }; },
        closest() { return this; },
        appendChild() {},
        remove() {},
        querySelectorAll() { return []; },
        focus() {},
    };
}

function createHarness() {
    const { layout, model } = createSelectionTestLayout();
    layout.blocks.push({
        type: 'image',
        objectId: 'img-1',
        blockId: 'img-block-1',
        runId: 'img-run-1',
        pageIndex: 0,
        sequence: 99,
        rect: { x: 120, y: 400, width: 100, height: 80 },
        object: { objectId: 'img-1', kind: 'image' },
    });
    const root = fakeElement();
    const pageElement = fakeElement({ 'data-page-index': '0', 'data-canvas-page-zoom-scale': '1' });
    root.closest = () => pageElement;
    pageElement.closest = () => pageElement;
    const doc = fakeElement();
    doc.createElement = () => fakeElement();
    const keyInput = fakeElement();
    const reports = [];
    const controller = createCanvasSelectionController({
        document: doc,
        canvasStack: { root, pages: new Map() },
        inputBridge: { input: keyInput, focus() {}, subscribe: () => () => {} },
        onSelectionChanged: state => reports.push(state),
    });
    controller.mount();
    controller.update(layout, model);
    reports.length = 0;
    const point = { clientX: 150, clientY: 430, button: 0, detail: 1, target: pageElement, preventDefault() {} };
    return { controller, root, doc, keyInput, reports, point, layout, model };
}

const sources = reports => reports.map(report => report.objectSelectionSource);

test('a re-layout of a selected object is reported as layout, never as a deliberate selection', () => {
    const { controller, reports, root, point, doc, layout, model } = createHarness();
    root.dispatch('mousedown', point);
    doc.dispatch('mouseup', point);
    reports.length = 0;

    controller.update(layout, model);

    assert.deepEqual(sources(reports), ['layout']);
});

test('pointer down on an object reports layout, the release without a drag reports pointer exactly once', () => {
    const { reports, root, point, doc } = createHarness();

    root.dispatch('mousedown', point);
    assert.deepEqual(sources(reports), ['layout']);

    doc.dispatch('mouseup', point);
    assert.deepEqual(sources(reports), ['layout', 'pointer']);
    assert.equal(reports.at(-1).object.objectId, 'img-1');
});

test('the pointer stamp is one-shot: the next overlay render is a layout report again', () => {
    const { controller, reports, root, point, doc, layout, model } = createHarness();
    root.dispatch('mousedown', point);
    doc.dispatch('mouseup', point);
    reports.length = 0;

    controller.update(layout, model);
    controller.update(layout, model);

    assert.deepEqual(sources(reports), ['layout', 'layout']);
});

test('dragging an object reports layout for every preview and for the release (no deliberate report)', () => {
    const { reports, root, point, doc } = createHarness();
    root.dispatch('mousedown', point);
    reports.length = 0;

    doc.dispatch('mousemove', { ...point, clientX: 170, clientY: 450 });
    doc.dispatch('mouseup', { ...point, clientX: 170, clientY: 450 });

    assert.ok(reports.length >= 1);
    assert.deepEqual([...new Set(sources(reports))], ['layout']);
});

test('keyboard object cycling reports keyboard once', () => {
    const { reports, keyInput, root, point, doc } = createHarness();
    root.dispatch('mousedown', point);
    doc.dispatch('mouseup', point);
    reports.length = 0;

    keyInput.dispatch('keydown', { key: 'Tab', ctrlKey: true, shiftKey: false, altKey: false, metaKey: false, preventDefault() {}, stopPropagation() {} });

    assert.deepEqual(sources(reports).filter(source => source !== 'layout'), ['keyboard']);
});
