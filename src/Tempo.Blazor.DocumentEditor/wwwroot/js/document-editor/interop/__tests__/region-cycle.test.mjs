import assert from 'node:assert/strict';
import test from 'node:test';
import {
    REGION_ORDER,
    attachRegionCycle,
    currentRegionIndex,
    detachRegionCycle,
    focusRegion,
    handleRegionCycleKey,
    markRegionFocus,
    clearRegionFocus,
    REGION_FOCUS_MS,
    nextRegionIndex,
    observeRibbonSize,
    resolveRegions,
} from '../region-cycle.mjs';

// A minimal DOM: elements with attributes, children, contains(), querySelectorAll over a flat registry and focus().
function fakeElement(attrs = {}, { displayed = true, parent = null } = {}) {
    const element = {
        attrs,
        displayed,
        parent,
        children: [],
        focused: 0,
        listeners: [],
        getAttribute(name) { return name in this.attrs ? this.attrs[name] : null; },
        setAttribute(name, value) { this.attrs[name] = String(value); },
        removeAttribute(name) { delete this.attrs[name]; },
        hasAttribute(name) { return name in this.attrs; },
        getClientRects() { return this.displayed ? [{}] : []; },
        contains(other) {
            for (let node = other; node; node = node.parent) if (node === this) return true;
            return false;
        },
        focus() { this.focused += 1; fakeDocument.activeElement = this; },
        closest(selector) {
            // only the [aria-modal="true"] probe is used
            for (let node = this; node; node = node.parent) if (selector.includes('aria-modal') && node.attrs['aria-modal'] === 'true') return node;
            return null;
        },
        querySelectorAll(selector) { return this.matches ? this.matches(selector) : []; },
        addEventListener(type, handler, capture) { this.listeners.push({ type, handler, capture }); },
        removeEventListener(type, handler, capture) {
            this.listeners = this.listeners.filter(l => !(l.type === type && l.handler === handler && l.capture === capture));
        },
    };
    if (parent) parent.children.push(element);
    return element;
}

const fakeDocument = { activeElement: null };

function editor({ withNav = true, withHeader = true } = {}) {
    const root = fakeElement();
    const regions = {};
    for (const name of REGION_ORDER) {
        if (name === 'nav' && !withNav) continue;
        if (name === 'header' && !withHeader) continue;
        const region = fakeElement({ 'data-focus-region': name }, { parent: root });
        const control = fakeElement({}, { parent: region });
        region.control = control;
        region.matches = () => [control];
        regions[name] = region;
    }
    root.matches = selector => selector === '[data-focus-region]'
        ? Object.values(regions)
        : selector === '[data-region-focus]' ? Object.values(regions).filter(region => region.hasAttribute('data-region-focus')) : [];
    return { root, regions };
}

function keyEvent(overrides = {}, target = null) {
    return {
        key: 'F6', shiftKey: false, ctrlKey: false, altKey: false, metaKey: false, target,
        defaultPrevented: false, propagationStopped: false,
        preventDefault() { this.defaultPrevented = true; },
        stopPropagation() { this.propagationStopped = true; },
        ...overrides,
    };
}

test('the cycle order is header, ribbon, navigation, document, inspector', () => {
    assert.deepEqual(REGION_ORDER, ['header', 'ribbon', 'nav', 'doc', 'inspector']);
});

test('nextRegionIndex wraps in both directions and starts at the ends from outside', () => {
    assert.equal(nextRegionIndex(0, 5, false), 1);
    assert.equal(nextRegionIndex(4, 5, false), 0);
    assert.equal(nextRegionIndex(0, 5, true), 4);
    assert.equal(nextRegionIndex(3, 5, true), 2);
    assert.equal(nextRegionIndex(-1, 5, false), 0);
    assert.equal(nextRegionIndex(-1, 5, true), 4);
    assert.equal(nextRegionIndex(0, 0, false), -1);
});

test('resolveRegions keeps the order and skips absent and undisplayed regions', () => {
    const { root, regions } = editor({ withNav: false });
    regions.ribbon.displayed = false;
    assert.deepEqual(resolveRegions(root).map(r => r.name), ['header', 'doc', 'inspector']);
});

test('F6 moves focus to the next region and cancels the browser default', () => {
    const { root, regions } = editor();
    fakeDocument.activeElement = regions.header.control;
    const event = keyEvent({}, regions.header.control);
    assert.equal(handleRegionCycleKey(event, root, fakeDocument), true);
    assert.equal(event.defaultPrevented, true);
    assert.equal(event.propagationStopped, true);
    assert.equal(regions.ribbon.control.focused, 1);
});

test('Shift+F6 goes back and wraps from the first region to the last', () => {
    const { root, regions } = editor();
    fakeDocument.activeElement = regions.header.control;
    handleRegionCycleKey(keyEvent({ shiftKey: true }, regions.header.control), root, fakeDocument);
    assert.equal(regions.inspector.control.focused, 1);
});

test('the full forward cycle visits every region once and returns to the header', () => {
    const { root, regions } = editor();
    fakeDocument.activeElement = regions.header.control;
    const visited = [];
    for (let i = 0; i < 5; i += 1) {
        handleRegionCycleKey(keyEvent({}, fakeDocument.activeElement), root, fakeDocument);
        visited.push(REGION_ORDER.find(name => regions[name].control === fakeDocument.activeElement));
    }
    assert.deepEqual(visited, ['ribbon', 'nav', 'doc', 'inspector', 'header']);
});

test('without the navigation region (tablet) the cycle skips it', () => {
    const { root, regions } = editor({ withNav: false });
    fakeDocument.activeElement = regions.ribbon.control;
    handleRegionCycleKey(keyEvent({}, regions.ribbon.control), root, fakeDocument);
    assert.equal(regions.doc.control.focused, 1);
});

test('focus outside every region enters at the first region (the last one backwards)', () => {
    const { root, regions } = editor();
    const outside = fakeElement();
    fakeDocument.activeElement = outside;
    handleRegionCycleKey(keyEvent({}, outside), root, fakeDocument);
    assert.equal(regions.header.control.focused, 1);
    fakeDocument.activeElement = outside;
    handleRegionCycleKey(keyEvent({ shiftKey: true }, outside), root, fakeDocument);
    assert.equal(regions.inspector.control.focused, 1);
});

test('other keys and modified F6 are ignored and keep their default', () => {
    const { root, regions } = editor();
    fakeDocument.activeElement = regions.header.control;
    for (const overrides of [{ key: 'F5' }, { key: 'F6', ctrlKey: true }, { key: 'F6', altKey: true }, { key: 'F6', metaKey: true }]) {
        const event = keyEvent(overrides, regions.header.control);
        assert.equal(handleRegionCycleKey(event, root, fakeDocument), false);
        assert.equal(event.defaultPrevented, false);
    }
});

test('a modal surface owns F6 while it is up', () => {
    const { root, regions } = editor();
    const dialog = fakeElement({ 'aria-modal': 'true' });
    const inside = fakeElement({}, { parent: dialog });
    fakeDocument.activeElement = inside;
    const event = keyEvent({}, inside);
    assert.equal(handleRegionCycleKey(event, root, fakeDocument), false);
    assert.equal(event.defaultPrevented, false);
    assert.equal(regions.header.control.focused, 0);
});

test('currentRegionIndex finds the containing region', () => {
    const { root, regions } = editor();
    const list = resolveRegions(root);
    assert.equal(currentRegionIndex(list, regions.doc.control), 3);
    assert.equal(currentRegionIndex(list, null), -1);
});

test('focusRegion falls back to the region element when it has no focusable control', () => {
    const region = fakeElement({ 'data-focus-region': 'nav' });
    region.matches = () => [];
    assert.equal(focusRegion({ name: 'nav', element: region }), true);
    assert.equal(region.focused, 1);
});

test('attach registers one capture listener per root, detach removes it', () => {
    const { root } = editor();
    attachRegionCycle(root);
    attachRegionCycle(root);
    assert.equal(root.listeners.length, 2, 'keydown (F6) and pointerdown (clears the ring)');
    assert.deepEqual(root.listeners.map(l => l.type).sort(), ['keydown', 'pointerdown']);
    assert.equal(root.listeners.every(l => l.capture === true), true);
    detachRegionCycle(root);
    assert.equal(root.listeners.length, 0);
    detachRegionCycle(root);
});

// A control that matches the given selectors, in DOM order inside a region whose querySelectorAll honours the selector.
function regionWith(name, controls) {
    const region = fakeElement({ 'data-focus-region': name });
    for (const control of controls) {
        control.parent = region;
        region.children.push(control);
    }
    region.matches = selector => region.children.filter(child => child.selectors.includes(selector));
    return region;
}

function control(label, selectors) {
    const element = fakeElement({ label });
    element.selectors = selectors;
    return element;
}

for (const name of ['ribbon', 'inspector']) {
    test(`${name}: the selected tab wins over a plain button and an unselected tab that come first in DOM order`, () => {
        const plainButton = control('button', ['button:not([disabled])', '[role="tab"]'.replace('[role="tab"]', 'button:not([disabled])')]);
        const unselected = control('tab-1', ['[role="tab"]', 'button:not([disabled])']);
        const selected = control('tab-2', ['[role="tab"][aria-selected="true"]', '[role="tab"]', 'button:not([disabled])']);
        const region = regionWith(name, [plainButton, unselected, selected]);

        assert.equal(focusRegion({ name, element: region }), true);
        assert.equal(selected.focused, 1, 'the selected tab took the focus');
        assert.equal(plainButton.focused + unselected.focused, 0);
    });

    test(`${name}: without a selected tab the first tab beats a plain button`, () => {
        const plainButton = control('button', ['button:not([disabled])']);
        const tab = control('tab', ['[role="tab"]', 'button:not([disabled])']);
        const region = regionWith(name, [plainButton, tab]);

        focusRegion({ name, element: region });

        assert.equal(tab.focused, 1);
        assert.equal(plainButton.focused, 0);
    });
}

test('the region F6 enters carries a visible ring that a pointer action or the timeout clears', () => {
    const { root, regions } = editor();
    fakeDocument.activeElement = regions.header.control;
    const timers = [];
    handleRegionCycleKey(keyEvent({}, regions.header.control), root, fakeDocument);
    assert.equal(regions.ribbon.getAttribute('data-region-focus'), 'true');
    assert.equal(regions.header.getAttribute('data-region-focus'), null);

    // the next F6 moves the ring with the focus
    handleRegionCycleKey(keyEvent({}, regions.ribbon.control), root, fakeDocument);
    assert.equal(regions.ribbon.getAttribute('data-region-focus'), null);
    assert.equal(regions.nav.getAttribute('data-region-focus'), 'true');

    clearRegionFocus(root);
    assert.equal(regions.nav.getAttribute('data-region-focus'), null);

    markRegionFocus({ name: 'doc', element: regions.doc }, fn => { timers.push(fn); return timers.length; }, () => {});
    assert.equal(regions.doc.getAttribute('data-region-focus'), 'true');
    timers[0]();
    assert.equal(regions.doc.getAttribute('data-region-focus'), null, `the ring fades after ${REGION_FOCUS_MS} ms`);
});

test('observeRibbonSize publishes the ribbon height once per change and cleans up', () => {
    const properties = new Map();
    const ribbon = fakeElement({ 'data-focus-region': 'ribbon' });
    let height = 96;
    ribbon.getBoundingClientRect = () => ({ height });
    const root = fakeElement();
    root.style = { setProperty: (k, v) => properties.set(k, v), removeProperty: k => properties.delete(k) };
    root.querySelector = selector => selector === '[data-focus-region="ribbon"]' ? ribbon : null;
    let callback = null;
    let disconnected = false;
    class FakeObserver { constructor(cb) { callback = cb; } observe() {} disconnect() { disconnected = true; } }

    const stop = observeRibbonSize(root, FakeObserver);
    assert.equal(properties.get('--tm-document-ribbon-block-size'), '96px');

    let writes = 0;
    const original = root.style.setProperty;
    root.style.setProperty = (k, v) => { writes += 1; original(k, v); };
    callback();
    assert.equal(writes, 0, 'an unchanged height writes nothing');
    height = 140;
    callback();
    assert.equal(properties.get('--tm-document-ribbon-block-size'), '140px');

    stop();
    assert.equal(disconnected, true);
    assert.equal(properties.has('--tm-document-ribbon-block-size'), false);
});
