// exportSvg paint baking: wireframe-designer.js evaluated in a vm context with a hand-rolled flat
// SVG tree (no jsdom). The live and cloned trees are paired by document order, so the UI-chrome
// sweep (.remove()) must run AFTER the paints are baked - otherwise a selected connector's
// waypoint handles shift the pairing and the next connector's label gets the wrong paint.
import test from 'node:test';
import assert from 'node:assert/strict';
import vm from 'node:vm';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const here = path.dirname(fileURLToPath(import.meta.url));
const SRC_PATH = path.resolve(here, '../wireframe-designer.js');
const SRC = fs.readFileSync(SRC_PATH, 'utf8');

function loadEngine(window = {}) {
    const win = Object.assign({}, window);
    const ctx = vm.createContext({ window: win, document: {}, console, setTimeout, clearTimeout, performance });
    vm.runInContext(SRC, ctx, { filename: SRC_PATH });
    return win.tmWireframeDesigner;
}

/** A flat fake tree: nodes carry classes + attributes + a style bag; querySelectorAll supports
 *  `[attr]` and a comma list of `.class` selectors, in document order. */
function makeTree(specs) {
    const nodes = specs.map(spec => makeNode(spec));
    const tree = {
        nodes,
        querySelectorAll(selector) {
            const parts = selector.split(',').map(s => s.trim());
            return tree.nodes.filter(n => parts.some(part => {
                if (part.startsWith('[')) return n.attrs.has(part.slice(1, -1));
                if (part.startsWith('.')) return n.classes.has(part.slice(1));
                return false;
            }));
        },
        cloneNode() { return makeTree(specs.map(spec => ({ ...spec }))); },
        // The root element's own attributes (the export rewrites viewBox / width / height / style on it).
        rootAttrs: new Map(),
        setAttribute(name, value) { tree.rootAttrs.set(name, String(value)); },
        removeAttribute(name) { tree.rootAttrs.delete(name); },
        get outerHTML() {
            return tree.nodes.map(n => `<${n.id}${[...n.attrs].map(([k, v]) => ` ${k}="${v}"`).join('')}/>`).join('');
        },
    };
    tree.nodes.forEach(n => { n.tree = tree; });
    return tree;
}

function makeNode({ id, classes = [], paint = null, computed = {} }) {
    const attrs = new Map();
    if (paint) attrs.set('data-wd-paint', paint);
    const style = {};
    return {
        id, classes: new Set(classes), attrs, computed, tree: null,
        getAttribute(name) { return attrs.has(name) ? attrs.get(name) : null; },
        setAttribute(name, value) { attrs.set(name, value); },
        removeAttribute(name) { attrs.delete(name); },
        remove() { this.tree.nodes = this.tree.nodes.filter(n => n !== this); },
        style: { removeProperty() {} },
    };
}

test('a selected connector with waypoint handles does not shift the label paint pairing', () => {
    const PAPER_INK = 'rgb(17, 24, 39)';
    const specs = [
        // connector 1 (selected): its path, then waypoint handles carrying data-wd-paint chrome paints
        { id: 'path1', paint: 'stroke', computed: { stroke: 'rgb(1, 1, 1)' } },
        { id: 'wp1', classes: ['tm-wd-waypoint-handle'], paint: 'fill stroke', computed: { fill: 'rgb(250, 0, 0)', stroke: 'rgb(250, 0, 0)' } },
        { id: 'wp2', classes: ['tm-wd-waypoint-handle'], paint: 'fill stroke', computed: { fill: 'rgb(250, 0, 0)', stroke: 'rgb(250, 0, 0)' } },
        // connector 2 with a label
        { id: 'label2', classes: ['tm-wd-connector__label'], paint: 'fill', computed: { fill: PAPER_INK } },
    ];
    const live = makeTree(specs);
    // The live tree answers getComputedStyle from each node's `computed` bag.
    const engine = loadEngine({
        getComputedStyle: node => ({ getPropertyValue: prop => node.computed[prop] ?? '' }),
    });
    live.id = 'wd1';
    live.cloneNode = () => makeTree(specs.map(spec => ({ ...spec, computed: {} })));
    engine.instances.set('wd1', { svg: live, canvasW: 800, canvasH: 600 });

    const html = engine.exportSvg({ id: 'wd1' });

    assert.match(html, new RegExp(`<label2[^>]*fill="${PAPER_INK.replace(/[()]/g, '\\$&')}"`), html);
    assert.doesNotMatch(html, /wp1|wp2/, 'waypoint chrome is stripped');
    assert.doesNotMatch(html, /data-wd-paint/, 'the paint marker is removed from the export');
    assert.match(html, /<path1[^>]*stroke="rgb\(1, 1, 1\)"/, html);
});

// ── The exported svg is a standalone PAGE image, whatever the live view is (InitialZoom fits / pans / zooms the live viewBox) ──

function exportRoot(attrsOfClone) {
    const store = new Map(Object.entries(attrsOfClone));
    const clone = {
        querySelectorAll() { return []; },
        getAttribute: n => (store.has(n) ? store.get(n) : null),
        setAttribute: (n, v) => store.set(n, String(v)),
        removeAttribute: n => store.delete(n),
        get outerHTML() { return '<svg ' + [...store].map(([k, v]) => `${k}="${v}"`).join(' ') + '/>'; },
        store,
    };
    const live = { cloneNode: () => clone, querySelectorAll: () => [] };
    const engine = loadEngine({ getComputedStyle: () => ({ getPropertyValue: () => '' }) });
    engine.instances.set('wd1', { svg: live, canvasW: 1440, canvasH: 900 });
    return { engine, clone };
}

test('exportSvg resets the root to the page: viewBox 0 0 W H, explicit width / height, no live sizing style', () => {
    const { engine, clone } = exportRoot({ viewBox: '-40 -61.5 1360 923', style: 'width:100%;height:100%;display:block;overflow:hidden;', class: 'tm-wd-canvas__svg', id: 'tm-wd-abc' });

    const html = engine.exportSvg({ id: 'wd1' });

    assert.equal(clone.getAttribute('viewBox'), '0 0 1440 900', 'a fitted / panned live view never leaks into the file');
    assert.equal(clone.getAttribute('width'), '1440');
    assert.equal(clone.getAttribute('height'), '900');
    assert.equal(clone.getAttribute('style'), null, 'width:100%;height:100% would make the image 100 % of nothing');
    assert.doesNotMatch(html, /overflow:hidden/);
    assert.match(html, /^<svg /);
});

test('exportSvg keeps the xmlns so the rasteriser can read the file', () => {
    const { engine, clone } = exportRoot({ viewBox: '0 0 800 600', xmlns: 'http://www.w3.org/2000/svg' });
    engine.exportSvg({ id: 'wd1' });
    assert.equal(clone.getAttribute('xmlns'), 'http://www.w3.org/2000/svg');
});