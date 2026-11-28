import test from 'node:test';
import assert from 'node:assert/strict';
import { buildFormattingState } from './format-state.mjs';

test('empty / missing command-state yields safe defaults (primitives only)', () => {
    const f = buildFormattingState(null);
    assert.equal(f.bold, false);
    assert.equal(f.boldMixed, false);
    assert.equal(f.fontFamily, '');
    assert.equal(f.fontSize, '');
    assert.equal(f.alignment, 'left');
    assert.equal(f.lineSpacing, 1);
    assert.equal(f.blockStyle, 'Normal');
    assert.equal(f.showRuler, true);
    assert.equal(f.image, null);
    // every value must be a primitive (or null for image) so the payload stays tiny across interop
    for (const [key, value] of Object.entries(f)) {
        const t = typeof value;
        assert.ok(
            t === 'boolean' || t === 'number' || t === 'string' || value === null,
            `field ${key} must be a primitive, got ${t}`);
    }
});

test('active marks map to active=true / mixed=false', () => {
    const f = buildFormattingState({ commands: { bold: { active: true }, italic: { active: true, mixed: false } } });
    assert.equal(f.bold, true);
    assert.equal(f.boldMixed, false);
    assert.equal(f.italic, true);
});

test('mixed marks map to mixed=true', () => {
    const f = buildFormattingState({ commands: { bold: { active: false, mixed: true } } });
    assert.equal(f.bold, false);
    assert.equal(f.boldMixed, true);
});

test('font + colour values pass through from commands', () => {
    const f = buildFormattingState({
        commands: {
            fontfamily: { value: 'Georgia, serif' },
            fontsize: { value: '24' },
            textcolor: { value: '#123456' },
            highlight: { value: '#fff59d' },
        },
    });
    assert.equal(f.fontFamily, 'Georgia, serif');
    assert.equal(f.fontSize, '24');
    assert.equal(f.textColor, '#123456');
    assert.equal(f.highlightColor, '#fff59d');
});

test('alignment + list fall back to paragraph block state when no command value', () => {
    const f = buildFormattingState({
        commands: {},
        paragraph: { alignment: 'center', bulletList: true, blockStyle: 'Heading1' },
    });
    assert.equal(f.alignment, 'center');
    assert.equal(f.bulletList, true);
    assert.equal(f.blockStyle, 'Heading1');
});

test('command value wins over paragraph fallback for alignment', () => {
    const f = buildFormattingState({
        commands: { align: { value: 'right' } },
        paragraph: { alignment: 'center' },
    });
    assert.equal(f.alignment, 'right');
});

test('buildFormattingState reports zoomAutoFit (off by default) next to the zoom preset', () => {
    assert.equal(buildFormattingState(null).zoomAutoFit, 'off');
    const f = buildFormattingState({ view: { zoomPercent: 87, zoomPreset: 'fitWidth', zoomAutoFit: 'narrow' } });
    assert.equal(f.zoomAutoFit, 'narrow');
    assert.equal(f.zoomPreset, 'fitWidth');
    assert.equal(f.zoomPercent, 87);
    assert.equal(buildFormattingState({ view: { zoom: { percent: 60, preset: 'fitWidth', autoFit: 'always' } } }).zoomAutoFit, 'always');
});
