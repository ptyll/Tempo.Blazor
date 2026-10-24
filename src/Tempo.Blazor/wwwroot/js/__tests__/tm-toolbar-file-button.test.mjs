// File-chooser trigger of TmToolbarFileButton (colocated module). Hand-rolled DOM stubs, no jsdom.
import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveInput, acquire, release, __resetForTests } from '../../../Components/Toolbar/TmToolbarFileButton.razor.js';

test.beforeEach(() => {
    __resetForTests();
    delete globalThis.document;
});

/** A DOM stub: elements by id, a click target that answers closest(), and a recorded document-level listener. */
function dom() {
    const byId = new Map();
    const listeners = [];
    globalThis.document = {
        getElementById: id => byId.get(id) ?? null,
        addEventListener: (type, fn, capture) => listeners.push({ type, fn, capture }),
        removeEventListener: (type, fn) => {
            const index = listeners.findIndex(l => l.type === type && l.fn === fn);
            if (index >= 0) listeners.splice(index, 1);
        },
    };
    return { byId, listeners };
}

const input = (id, extra = {}) => ({ id, value: 'old.json', disabled: false, clicks: 0, click() { this.clicks++; }, ...extra });

/**
 * A trigger element; closest() returns itself when it matches one of the selector's parts: [attr] or .class[attr]
 * (the class list comes from attrs.class, default none).
 */
const trigger = attrs => ({
    disabled: attrs.disabled === true,
    getAttribute: name => attrs[name] ?? null,
    closest(selector) {
        const classes = (attrs.class ?? '').split(' ');
        const matches = selector.split(',').map(s => s.trim()).some(part => {
            const m = /^(?:\.([\w-]+))?\[([\w-]+)\]$/.exec(part);
            return !!m && (!m[1] || classes.includes(m[1])) && this.getAttribute(m[2]) !== null;
        });
        return matches ? this : null;
    },
});

test('a click on the bar button opens the input named by data-tm-file-trigger', () => {
    const el = input('in1');
    const found = resolveInput(trigger({ 'data-tm-file-trigger': 'in1' }), id => (id === 'in1' ? el : null));
    assert.equal(found, el);
});

test('a click on the More entry opens the input registered for its data-action-id', () => {
    const el = input('in1');
    acquire('in1', 'entry-1');
    const found = resolveInput(trigger({ 'data-action-id': 'entry-1', class: 'tm-toolbar-more__item' }), id => (id === 'in1' ? el : null));
    assert.equal(found, el);
});

test('a More entry nobody registered is not ours', () => {
    const el = input('in1');
    acquire('in1', 'entry-1');
    assert.equal(resolveInput(trigger({ 'data-action-id': 'other-entry', class: 'tm-toolbar-more__item' }), () => el), null);
});

test('a data-action-id outside the More menu (another menu host, a mobile action bar) is not ours', () => {
    const el = input('in1');
    acquire('in1', 'entry-1');
    assert.equal(resolveInput(trigger({ 'data-action-id': 'entry-1' }), () => el), null);
});

test('a click elsewhere resolves nothing', () => {
    assert.equal(resolveInput({ closest: () => null }, () => input('in1')), null);
    assert.equal(resolveInput(null, () => input('in1')), null);
});

test('a disabled trigger or a disabled input never opens the chooser', () => {
    assert.equal(resolveInput(trigger({ 'data-tm-file-trigger': 'in1', disabled: true }), () => input('in1')), null);
    assert.equal(resolveInput(trigger({ 'data-tm-file-trigger': 'in1', 'aria-disabled': 'true' }), () => input('in1')), null);
    assert.equal(resolveInput(trigger({ 'data-tm-file-trigger': 'in1' }), () => input('in1', { disabled: true })), null);
});

test('a trigger whose input left the DOM resolves nothing', () => {
    assert.equal(resolveInput(trigger({ 'data-tm-file-trigger': 'gone' }), () => null), null);
});

test('acquire installs ONE capture-phase click listener for any number of buttons; the last release removes it', () => {
    const { byId, listeners } = dom();
    byId.set('a', input('a'));
    byId.set('b', input('b'));
    acquire('a', 'ea');
    acquire('b', 'eb');
    acquire('a', 'ea');   // idempotent
    assert.equal(listeners.length, 1);
    assert.equal(listeners[0].type, 'click');
    assert.equal(listeners[0].capture, true);

    release('a');
    assert.equal(listeners.length, 1, 'b is still registered');
    release('b');
    assert.equal(listeners.length, 0);
});

test('the listener clears the previous selection and clicks the input synchronously', () => {
    const { byId, listeners } = dom();
    const el = input('in1');
    byId.set('in1', el);
    acquire('in1', 'entry-1');

    listeners[0].fn({ target: trigger({ 'data-tm-file-trigger': 'in1' }) });

    assert.equal(el.clicks, 1);
    assert.equal(el.value, '', 're-importing the same file must raise a change again');
});

test('the listener ignores a click that is not on a file trigger', () => {
    const { byId, listeners } = dom();
    const el = input('in1');
    byId.set('in1', el);
    acquire('in1', 'entry-1');

    listeners[0].fn({ target: { closest: () => null } });

    assert.equal(el.clicks, 0);
    assert.equal(el.value, 'old.json');
});

test('a re-acquire with a new entry id replaces the old one', () => {
    acquire('in1', 'old-entry');
    acquire('in1', 'new-entry');
    const el = input('in1');
    assert.equal(resolveInput(trigger({ 'data-action-id': 'old-entry', class: 'tm-toolbar-more__item' }), () => el), null);
    assert.equal(resolveInput(trigger({ 'data-action-id': 'new-entry', class: 'tm-toolbar-more__item' }), () => el), el);
});

test('a file input that refuses a value write still opens', () => {
    const { byId, listeners } = dom();
    const el = input('in1');
    Object.defineProperty(el, 'value', { get: () => 'x', set: () => { throw new Error('InvalidStateError'); } });
    byId.set('in1', el);
    acquire('in1', 'e');

    listeners[0].fn({ target: trigger({ 'data-tm-file-trigger': 'in1' }) });

    assert.equal(el.clicks, 1);
});

test('release of an unknown input is a no-op', () => {
    assert.doesNotThrow(() => release('never-acquired'));
});

test('release prunes registrations whose input left the DOM, so the listener cannot outlive the buttons', () => {
    const { byId, listeners } = dom();
    byId.set('a', input('a'));
    byId.set('b', input('b'));
    acquire('a', 'ea');
    acquire('b', 'eb');
    assert.equal(listeners.length, 1);

    byId.delete('a');             // the circuit died: release('a') never arrived
    release('b');                 // the release that did arrive sweeps the ghost, so the last LIVE button removes the listener
    assert.equal(resolveInput(trigger({ 'data-action-id': 'ea', class: 'tm-toolbar-more__item' }), () => input('a')), null,
        'the pruned entry id no longer resolves');
    assert.equal(listeners.length, 0, 'no ghost keeps the document listener alive');
});

test('acquire does not scan the DOM: a registration whose input is not (yet) in the DOM survives it', () => {
    const { byId, listeners } = dom();
    let lookups = 0;
    const real = globalThis.document.getElementById;
    globalThis.document.getElementById = id => { lookups++; return real(id); };
    acquire('a', 'ea');           // registered before its input rendered: must not be pruned by the next acquire
    acquire('b', 'eb');
    assert.equal(lookups, 0, 'acquire never calls getElementById');
    byId.set('a', input('a'));
    assert.equal(resolveInput(trigger({ 'data-action-id': 'ea', class: 'tm-toolbar-more__item' }), id => byId.get(id) ?? null)?.id, 'a');
    assert.equal(listeners.length, 1);
});