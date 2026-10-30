// Node unit tests for the Tempo.Blazor.Spreadsheet canvas LIVE THEME watcher (redesign phase 7c,
// finding 18f08937): the palette used to be rebuilt only by the ResizeObserver and by initEngine, so a
// dark/light or indigo flip on a live page recoloured the chrome while the canvas (headers, selection)
// kept the OLD colours until a resize happened. The engine now watches the two theme attributes on
// documentElement and repaints; this lane proves the wiring with a fake MutationObserver instead of a
// browser (the browser side is the E2E spec `Phase7c_LiveThemeFlip_RepaintsTheCanvasWithoutResize`).
//
// Run: npm run test:spreadsheet
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { test } from 'node:test';
import vm from 'node:vm';

const CANVAS_SCRIPT = new URL('../spreadsheet-canvas.js', import.meta.url);
const SOURCE = readFileSync(CANVAS_SCRIPT, 'utf8');

/**
 * Loads the shipped canvas script into a sandbox. `tokens` is what getComputedStyle(root) reports for
 * a `var(--tm-*)` lookup; `env` injects the browser globals a test wants to observe (document,
 * MutationObserver) so the shipped watcher runs unmodified.
 */
function loadCanvas(tokens = {}, env = {}) {
    const root = { __tokens: tokens };
    const sandbox = {
        Element: class StubElement {},
        window: {},
        getComputedStyle: element => ({
            getPropertyValue: name => (element?.__tokens?.[name] ?? '').trim(),
        }),
        performance: { now: () => 0 },
        requestAnimationFrame: () => 0,
        cancelAnimationFrame: () => undefined,
        setTimeout: () => 0,
        clearTimeout: () => undefined,
        setInterval: () => 0,
        clearInterval: () => undefined,
        console,
    };
    if ('document' in env) sandbox.document = env.document;
    if ('MutationObserver' in env) sandbox.MutationObserver = env.MutationObserver;
    vm.createContext(sandbox);
    vm.runInContext(SOURCE, sandbox, { filename: 'spreadsheet-canvas.js' });
    const internals = sandbox.window.tmSpreadsheetCanvas?._internals;
    assert.ok(internals, 'spreadsheet-canvas.js must expose window.tmSpreadsheetCanvas._internals');
    return { internals, root };
}

/** A fake element whose classList reflects a mutable set (the shipped code only calls contains()). */
function fakeThemeNode(name) {
    const classes = new Set();
    return {
        tagName: name,
        classes,
        classList: { contains: value => classes.has(value) },
    };
}

/** Records every observer the shipped code creates so the test can fire each mutation callback. */
function fakeObserverEnvironment() {
    const observers = [];
    const documentElement = fakeThemeNode('HTML');
    const body = fakeThemeNode('BODY');
    class FakeMutationObserver {
        constructor(callback) {
            this.callback = callback;
            this.disconnected = false;
            observers.push(this);
        }

        observe(target, options) {
            this.target = target;
            this.options = options;
            // A real MutationObserver keeps ONE callback for several observed nodes (the class watcher
            // covers <html> and <body>), so every target is recorded.
            (this.targets = this.targets || []).push(target);
        }

        disconnect() {
            this.disconnected = true;
        }
    }

    return {
        observers,
        documentElement,
        body,
        environment: { document: { documentElement, body }, MutationObserver: FakeMutationObserver },
    };
}

const LIGHT = {
    '--tm-color-surface': '#ffffff',
    '--tm-color-primary': '#2f2fbf',
    '--tm-spreadsheet-header-bg': '#eeeeee',
    '--tm-spreadsheet-header-text': '#555555',
};

const DARK = {
    '--tm-color-surface': '#101010',
    '--tm-color-primary': '#9999ff',
    '--tm-spreadsheet-header-bg': '#181818',
    '--tm-spreadsheet-header-text': '#cccccc',
};

test('themeAttributeChange_RebuildsThePalette', () => {
    const { observers, documentElement, environment } = fakeObserverEnvironment();
    const { internals, root } = loadCanvas(LIGHT, environment);
    assert.equal(typeof internals.observeLiveTheme, 'function',
        '_internals.observeLiveTheme is the node-tested seam of the live theme watcher');
    assert.equal(typeof internals.stateKey, 'string', '_internals.stateKey exposes the engine state slot');

    // The engine state the watcher mutates (the same slot register() fills in the browser).
    const state = { model: {}, palette: internals.buildPalette(root) };
    root[internals.stateKey] = state;
    assert.equal(state.palette.surface, '#ffffff');

    const session = internals.observeLiveTheme(root);
    assert.ok(session, 'a live page gets a theme session');
    assert.equal(session.observers.length, 2,
        'ONE attribute observer + ONE class observer for the tm-dark switch (never one per node)');
    assert.equal(observers.length, 2, 'two observers per grid instance, no more');
    assert.equal(observers[0].target, documentElement,
        'the attribute observer watches documentElement, where data-theme / data-tm-theme are set');
    assert.equal(observers[0].options.attributes, true);
    assert.deepEqual(Array.from(observers[0].options.attributeFilter), ['data-theme', 'data-tm-theme'],
        'both theme attributes are watched: light/dark flips data-theme, indigo flips data-tm-theme');
    assert.equal(observers[0].options.subtree, true,
        'subtree: the demo shell (and many hosts) put data-theme on a wrapper, not on <html>');

    // The live flip: the theme layer re-resolves the tokens, the attributes change, nothing resizes.
    root.__tokens = DARK;
    observers[0].callback([{ type: 'attributes', attributeName: 'data-theme' }]);

    assert.equal(state.palette.surface, '#101010',
        'the palette is rebuilt from the NEW tokens on the attribute change (no resize in between)');
    assert.equal(state.palette.headerBg, '#181818');
    assert.equal(state.palette.headerText, '#cccccc');
    assert.equal(state.dirtyReason, 'theme-change', 'the repaint is attributed to the theme flip');
    assert.equal(state.dirtyKind, 'full', 'a full redraw: the header, selection and content layers all read the palette');
});

test('themeAttributeChange_IndigoAttribute_AlsoRebuildsThePalette', () => {
    const { observers, environment } = fakeObserverEnvironment();
    const { internals, root } = loadCanvas(LIGHT, environment);
    const state = { model: {}, palette: internals.buildPalette(root) };
    root[internals.stateKey] = state;

    internals.observeLiveTheme(root);
    root.__tokens = { ...LIGHT, '--tm-color-primary': '#4f46e5' };
    observers[0].callback([{ type: 'attributes', attributeName: 'data-tm-theme' }]);

    assert.equal(state.palette.primary, '#4f46e5', 'data-tm-theme=indigo repaints the canvas primary too');
    assert.equal(state.dirtyKind, 'full');
});

test('observeLiveTheme_WithoutABrowserSeam_IsANoOp', () => {
    const { internals, root } = loadCanvas(LIGHT);
    assert.equal(internals.observeLiveTheme(root), null,
        'no document / MutationObserver (an old host, the node lane) must degrade to the previous behaviour');
    assert.equal(internals.observeLiveTheme(null), null);
});

test('observeLiveTheme_DetachStopsTheWatcher', () => {
    const { observers, environment } = fakeObserverEnvironment();
    const { internals, root } = loadCanvas(LIGHT, environment);
    root[internals.stateKey] = { model: {}, palette: internals.buildPalette(root) };

    const session = internals.observeLiveTheme(root);
    assert.equal(internals.detachLiveTheme(session), true);
    assert.equal(observers.every(observer => observer.disconnected), true,
        'dispose must disconnect EVERY observer, or a re-created grid keeps repainting a dead state');
    assert.equal(internals.detachLiveTheme(null), false);
});

// ── the `.tm-dark` CLASS switch (Tempo's second public dark switch) ────────────────────────────

test('darkClassFlip_RebuildsThePalette_OnlyWhenTheClassReallyChanged', () => {
    const { observers, documentElement, body, environment } = fakeObserverEnvironment();
    const { internals, root } = loadCanvas(LIGHT, environment);
    const state = { model: {}, palette: internals.buildPalette(root) };
    root[internals.stateKey] = state;

    internals.observeLiveTheme(root);
    const classObserver = observers[1];
    assert.equal(classObserver.options.attributeFilter.join(','), 'class',
        'the class observer filters on the class attribute only');
    assert.notEqual(classObserver.options.subtree, true,
        'no subtree: a class observer over the whole document would fire on every hover/menu toggle');

    // The class switch lives on <html> OR <body>: the one observer covers exactly those two nodes.
    assert.deepEqual(classObserver.targets, [documentElement, body],
        'the class observer watches documentElement and body - and nothing else');

    // 1) `class` churn WITHOUT tm-dark: no repaint at all.
    state.palette = { surface: 'sentinel' };
    classObserver.callback([{ type: 'attributes', attributeName: 'class' }]);
    assert.equal(state.palette.surface, 'sentinel',
        'an unrelated class change (a menu, a layout helper) must NOT trigger a full canvas redraw');

    // 2) the real flip onto <html>.
    root.__tokens = DARK;
    documentElement.classes.add('tm-dark');
    classObserver.callback([{ type: 'attributes', attributeName: 'class' }]);
    assert.equal(state.palette.surface, '#101010',
        'html.tm-dark rebuilds the palette from the new tokens');
    assert.equal(state.dirtyKind, 'full');

    // 3) the same class state again: nothing changed, nothing repaints.
    state.palette = { surface: 'sentinel-2' };
    classObserver.callback([{ type: 'attributes', attributeName: 'class' }]);
    assert.equal(state.palette.surface, 'sentinel-2', 'an idempotent mutation list is ignored');

    // 4) back to light.
    root.__tokens = LIGHT;
    documentElement.classes.delete('tm-dark');
    classObserver.callback([{ type: 'attributes', attributeName: 'class' }]);
    assert.equal(state.palette.surface, '#ffffff', 'removing tm-dark repaints the light palette again');
});

test('darkClassOnBody_AlsoRebuildsThePalette', () => {
    const { observers, body, environment } = fakeObserverEnvironment();
    const { internals, root } = loadCanvas(LIGHT, environment);
    const state = { model: {}, palette: internals.buildPalette(root) };
    root[internals.stateKey] = state;

    internals.observeLiveTheme(root);
    // The same observer covers <html> and <body> (two observe() calls, one callback).
    assert.equal(internals.readDarkClassState({ document: { documentElement: { classList: { contains: () => false } }, body } }), false);

    root.__tokens = DARK;
    body.classes.add('tm-dark');
    assert.equal(internals.readDarkClassState({ document: { documentElement: { classList: { contains: () => false } }, body } }), true,
        'body.tm-dark is the same switch');
    observers[1].callback([{ type: 'attributes', attributeName: 'class' }]);
    assert.equal(state.palette.surface, '#101010', 'a body-level tm-dark repaints too');
});
