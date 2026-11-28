import test from 'node:test';
import assert from 'node:assert/strict';
import { updateRulerOverlay } from '../ruler.mjs';

// Phase 11: a page the engine fitted smaller than itself drags the ruler along - the track is as wide as the SCALED page and the markers sit at
// their scaled positions, so a ruler never makes the document column wider than the fitted page.
function createRoot() {
    const nodes = new Map();
    const make = key => {
        const attributes = new Map();
        const node = {
            style: {},
            setAttribute: (name, value) => attributes.set(name, String(value)),
            getAttribute: name => attributes.get(name) ?? null,
            querySelector: () => null,
            querySelectorAll: () => [],
            appendChild() {},
        };
        nodes.set(key, node);
        return node;
    };
    const track = make('track');
    const markers = ['margin-left', 'first-line-indent', 'left-indent', 'right-indent', 'margin-right']
        .map(name => make(`document-canvas-ruler-${name}`));
    const ruler = make('ruler');
    const root = {
        hidden: false,
        ownerDocument: { createElement: () => make(`created-${nodes.size}`) },
        setAttribute() {},
        querySelector: selector => {
            if (selector.includes('document-canvas-ruler"]')) {
                return ruler;
            }

            if (selector === '.tm-document-canvas-ruler__track') {
                return track;
            }

            const match = /data-testid="([^"]+)"/.exec(selector);
            return match ? nodes.get(match[1]) || null : null;
        },
    };
    return { root, track, marker: name => nodes.get(`document-canvas-ruler-${name}`) };
}

const layout = { pages: [{ width: 800, body: { x: 96, width: 608 } }] };
const model = { pageSettings: { marginLeft: 96, marginRight: 96 } };

test('ruler at 100 percent keeps the page width and the marker positions', () => {
    const { root, track, marker } = createRoot();
    updateRulerOverlay(root, layout, model, { zoom: { scale: 1 } });

    assert.equal(track.style.width, '800px');
    assert.equal(marker('margin-left').style.left, '96px');
    assert.equal(marker('margin-right').style.left, '704px');
});

test('ruler of a fitted page follows the scale: track width and marker positions are scaled, the reported position is not', () => {
    const { root, track, marker } = createRoot();
    updateRulerOverlay(root, layout, model, { zoom: { scale: 0.5 } });

    assert.equal(track.style.width, '400px');
    assert.equal(marker('margin-left').style.left, '48px');
    assert.equal(marker('margin-right').style.left, '352px');
    assert.equal(marker('margin-left').getAttribute('data-ruler-position'), '96', 'the data attribute stays in page pixels');
    assert.equal(root.__tmRulerState.scale, 0.5, 'the interaction divides pointer offsets by the scale');
});
