import test from 'node:test';
import assert from 'node:assert/strict';
import { attach } from '../../../../Tempo.Blazor/wwwroot/js/pointer-drag.js';
import { attachDesignerDrag, attachPaletteDrag } from '../pdf-template-designer-pointer.js';

function surface() {
    const listeners = new Map();
    const root = {
        style: {},
        contains(target) { return target?.inside !== false; },
        querySelector() { return { getBoundingClientRect: () => ({ width: 500, height: 800 }) }; },
        addEventListener(type, handler, capture) {
            const list = listeners.get(type) ?? [];
            list.push({ handler, capture: capture === true });
            listeners.set(type, list);
        },
        removeEventListener(type, handler) {
            const list = (listeners.get(type) ?? []).filter((item) => item.handler !== handler && item !== handler);
            listeners.set(type, list);
        },
        dispatch(type, event) {
            const list = listeners.get(type) ?? [];
            const invoke = (entry) => {
                const handler = entry.handler ?? entry;
                handler(event);
            };
            // Capture runs in registration order, then bubble. stopPropagation ends the phase
            // after the current handler, so a later capture listener still runs.
            for (const entry of list) {
                if ((entry.capture ?? false) !== true) continue;
                invoke(entry);
                if (event?.propagationStopped) return;
            }
            if (event?.propagationStopped) return;
            for (const entry of list) {
                if (entry.capture === true) continue;
                invoke(entry);
                if (event?.propagationStopped) return;
            }
        },
        listenerCount(type) { return (listeners.get(type) ?? []).length; },
    };
    root.ownerDocument = root;
    return root;
}

function field(uuid, handle, areaUuid) {
    const node = {
        offsetWidth: 80,
        offsetHeight: 40,
        style: { transform: '', width: '', height: '' },
        inside: true,
        closest(selector) {
            if (selector === '[data-field-uuid], [data-handle]') return { getAttribute: () => uuid };
            if (selector === '[data-field-uuid]') return { getAttribute: () => uuid, style: node.style, offsetWidth: 80, offsetHeight: 40 };
            if (selector === '[data-handle]') return handle ? { getAttribute: () => handle } : null;
            if (selector === '[data-area-uuid]') return areaUuid ? { getAttribute: () => areaUuid } : null;
            if (selector === '.tm-pdf-template-designer__page-surface') {
                return { getBoundingClientRect: () => ({ width: 500, height: 800 }) };
            }
            if (selector.includes('palette-item')) return null;
            return null;
        },
    };
    node.closest = node.closest.bind(node);
    return node;
}

test('a pointer drag commits normalized deltas on pointerup', () => {
    const element = surface();
    const commits = [];
    attachDesignerDrag(element, {
        attach,
        onCommit: (session) => commits.push(session),
    });

    const target = field('field-1', null, 'area-1');
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 1, clientX: 100, clientY: 200, pointerType: 'touch', target });
    element.dispatch('pointermove', { pointerId: 1, clientX: 150, clientY: 250, buttons: 1, target });
    assert.equal(commits.length, 0, 'a move mid-gesture must not commit');
    element.dispatch('pointerup', { pointerId: 1, clientX: 150, clientY: 250, target });

    assert.equal(commits.length, 1);
    assert.equal(commits[0].dx, 50);
    assert.equal(commits[0].normalizedDx, 0.1);
    assert.equal(commits[0].normalizedDy, 50 / 800);
    assert.equal(commits[0].fieldUuid, 'field-1');
    assert.equal(commits[0].areaUuid, 'area-1');
    assert.equal(commits[0].handle, null);
});

test('a resize drag reports the handle and a cancel commits nothing and restores the preview', () => {
    const element = surface();
    const commits = [];
    const session = attachDesignerDrag(element, {
        attach,
        onCommit: (commit) => commits.push(commit),
    });

    const target = field('field-1', 'SouthEast', 'area-2');
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 2, clientX: 10, clientY: 10, pointerType: 'mouse', target });
    element.dispatch('pointermove', { pointerId: 2, clientX: 40, clientY: 30, buttons: 1, target });
    assert.notEqual(target.style.width, '', 'a resize shows a live size');
    element.dispatch('pointercancel', { pointerId: 2, target });

    assert.equal(commits.length, 0);
    assert.equal(session.lastCommit(), null);
    assert.equal(target.style.width, '');
    assert.equal(target.style.height, '');
});

test('draw mode declines a field press so the page can place a field', () => {
    const element = surface();
    element.closest = (selector) => selector === '.tm-pdf-template-designer'
        ? { classList: { contains: (name) => name === 'tm-pdf-template-designer--drawing' } }
        : null;
    const commits = [];
    attachDesignerDrag(element, { attach, onCommit: (commit) => commits.push(commit) });

    const target = field('field-1', null, 'area-1');
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 7, clientX: 20, clientY: 20, pointerType: 'mouse', target });
    element.dispatch('pointermove', { pointerId: 7, clientX: 80, clientY: 60, buttons: 1, target });
    element.dispatch('pointerup', { pointerId: 7, clientX: 80, clientY: 60, target });

    assert.equal(commits.length, 0, 'draw mode owns the page');
    assert.equal(target.style.transform, '', 'a declined press leaves no live transform');
});

test('detach removes the workspace listeners and a second surface stays isolated', () => {
    const first = surface();
    const second = surface();
    const session = attachDesignerDrag(first, { attach });
    attachDesignerDrag(second, { attach });
    assert.ok(first.listenerCount('pointerdown') > 0);
    session.detach();
    assert.equal(first.listenerCount('pointerdown'), 0);
    assert.ok(second.listenerCount('pointerdown') > 0, 'the other surface keeps its own session');
});

test('a palette drag commits once and a press that does not move does not drop', () => {
    const element = surface();
    const surfaceNode = {
        getAttribute: () => 'attachment-1:0',
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 200, height: 400 }),
    };
    element.ownerDocument.elementFromPoint = () => ({
        closest: (selector) => selector.includes('page-surface') ? surfaceNode : null,
    });
    const drops = [];
    attachPaletteDrag(element, { attach, onDrop: (commit) => drops.push(commit) });

    const item = {
        inside: true,
        closest(selector) {
            return selector.includes('palette-item') ? item : null;
        },
    };
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 3, clientX: 8, clientY: 8, pointerType: 'touch', target: item });
    element.dispatch('pointerup', { pointerId: 3, clientX: 8, clientY: 8, target: item });
    assert.equal(drops.length, 0, 'a tap stays a tap');

    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 4, clientX: 8, clientY: 8, pointerType: 'mouse', target: item });
    element.dispatch('pointermove', { pointerId: 4, clientX: 40, clientY: 80, buttons: 1, target: item });
    element.dispatch('pointerup', { pointerId: 4, clientX: 40, clientY: 80, target: item });
    assert.equal(drops.length, 1);
    assert.equal(drops[0].pageKey, 'attachment-1:0');
});

test('a palette drag that ends off the page invokes a cancel and never a drop', () => {
    const element = surface();
    element.ownerDocument.elementFromPoint = () => null;
    const drops = [];
    const cancels = [];
    attachPaletteDrag(element, {
        attach,
        onDrop: (commit) => drops.push(commit),
        onCancel: () => cancels.push('cancel'),
    });

    const item = {
        inside: true,
        getAttribute: (name) => name === 'data-field-type' ? 'Signature' : null,
        closest(selector) {
            if (selector.includes('palette-item')) return item;
            if (selector.includes('page-surface')) return null;
            return null;
        },
    };
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 9, clientX: 8, clientY: 8, pointerType: 'mouse', target: item });
    element.dispatch('pointermove', { pointerId: 9, clientX: 80, clientY: 40, buttons: 1, target: item });
    element.dispatch('pointerup', { pointerId: 9, clientX: 400, clientY: 20, target: item });

    assert.equal(drops.length, 0, 'an off-page release places nothing');
    assert.equal(cancels.length, 1, 'an off-page release cancels the drag');
});

test('a palette press does not reach a page listener registered in the bubble phase', () => {
    const element = surface();
    const pagePresses = [];
    element.addEventListener('pointerdown', (event) => pagePresses.push(event.pointerId));
    attachPaletteDrag(element, { attach, onDrop: () => {} });

    const item = {
        inside: true,
        closest(selector) {
            return selector.includes('palette-item') ? item : null;
        },
    };
    const press = {
        button: 0,
        isPrimary: true,
        pointerId: 15,
        clientX: 8,
        clientY: 8,
        pointerType: 'mouse',
        target: item,
        stopPropagation() { this.propagationStopped = true; },
    };
    element.dispatch('pointerdown', press);

    assert.equal(pagePresses.length, 0, 'a palette press must not start a page draw or a selection box');
});

test('a west resize translates and shrinks, and a multi-select move previews every selected field', () => {
    const element = surface();
    const west = field('field-1', 'West', 'area-1');
    attachDesignerDrag(element, { attach });
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 11, clientX: 10, clientY: 10, pointerType: 'mouse', target: west });
    element.dispatch('pointermove', { pointerId: 11, clientX: 30, clientY: 10, buttons: 1, target: west });
    assert.match(west.style.transform, /translate/, 'a west handle moves the origin with the size');
    assert.notEqual(west.style.width, '', 'a west handle changes the width');
    element.dispatch('pointercancel', { pointerId: 11, target: west });
    assert.equal(west.style.transform, '');
    assert.equal(west.style.width, '');

    const peers = [field('field-a', null, 'area-a'), field('field-b', null, 'area-b')];
    for (const peer of peers) peer.classList = { contains: (name) => name === 'tm-signing-field--selected' };
    element.querySelectorAll = (selector) => selector === '.tm-signing-field--selected' ? peers : [];
    attachDesignerDrag(element, { attach });
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 12, clientX: 0, clientY: 0, pointerType: 'mouse', target: peers[0] });
    element.dispatch('pointermove', { pointerId: 12, clientX: 16, clientY: 8, buttons: 1, target: peers[0] });
    assert.match(peers[0].style.transform, /translate/);
    assert.match(peers[1].style.transform, /translate/, 'every selected field previews the move');
    element.dispatch('pointercancel', { pointerId: 12, target: peers[0] });
    assert.equal(peers[0].style.transform, '');
    assert.equal(peers[1].style.transform, '');
});

test('a 500 ms touch long-press reports a context menu and does not commit a move', (t) => {
    t.mock.timers.enable({ apis: ['setTimeout'] });
    const element = surface();
    const menus = [];
    const commits = [];
    attachDesignerDrag(element, {
        attach,
        onCommit: (commit) => commits.push(commit),
        onContextMenu: (info) => menus.push(info),
    });

    const target = field('field-1', null, 'area-1');
    element.dispatch('pointerdown', { button: 0, isPrimary: true, pointerId: 21, clientX: 40, clientY: 50, pointerType: 'touch', target });
    t.mock.timers.tick(500);
    element.dispatch('pointerup', { pointerId: 21, clientX: 40, clientY: 50, pointerType: 'touch', target });

    assert.equal(commits.length, 0, 'a long-press is a menu, not a move');
    assert.equal(menus.length, 1);
    assert.equal(menus[0].fieldUuid, 'field-1');
    assert.equal(menus[0].clientX, 40);
    assert.equal(menus[0].clientY, 50);
});
