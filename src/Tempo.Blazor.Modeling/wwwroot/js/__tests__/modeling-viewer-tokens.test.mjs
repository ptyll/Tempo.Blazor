import { test } from 'node:test';
import assert from 'node:assert/strict';

import {
  liveThemeOf,
  normalizeTokenNames,
  resolveTokensIn,
} from '../modeling-editor.js';

const HEAT_TOKENS = ['--tm-color-heat-0', '--tm-color-heat-3'];

/**
 * A fake document with a documentElement that RECORDS every mutation: the export must never flip
 * the page theme, so a touched documentElement is a test failure, not an implementation detail.
 */
function fakeDocument() {
  const mutations = [];
  const documentElement = {
    getAttribute: name => {
      mutations.push(`read:${name}`);
      return null;
    },
    setAttribute: (name, value) => mutations.push(`set:${name}=${value}`),
    removeAttribute: name => mutations.push(`remove:${name}`),
  };

  const created = [];
  return {
    documentElement,
    mutations,
    created,
    createElement(tagName) {
      const element = fakeProbe(tagName);
      created.push(element);
      return element;
    },
  };
}

/** A fake element good enough for `closest`, the attribute writes and the child bookkeeping. */
function fakeProbe(tagName = 'span') {
  const attributes = new Map();
  const classes = new Set();
  const children = [];
  const element = {
    tagName,
    parent: null,
    attributes,
    classes,
    children,
    classList: {
      add: name => classes.add(name),
    },
    closest(selector) {
      const wanted = String(selector || '').trim();
      const isSelf = wanted.startsWith('.')
        ? classes.has(wanted.slice(1))
        : attributes.has('data-theme');
      if (isSelf) {
        return element;
      }

      return element.parent && typeof element.parent.closest === 'function'
        ? element.parent.closest(selector)
        : null;
    },
    getAttribute: name => (attributes.has(name) ? attributes.get(name) : null),
    setAttribute: (name, value) => attributes.set(name, String(value)),
    removeAttribute: name => attributes.delete(name),
    appendChild(child) {
      child.parent = element;
      children.push(child);
      return child;
    },
    removeChild(child) {
      const index = children.indexOf(child);
      if (index < 0) {
        throw new Error('not a child');
      }

      children.splice(index, 1);
      child.parent = null;
      return child;
    },
    querySelector: () => null,
  };

  return element;
}

/** A root element that also behaves as the export's caller scope. */
function fakeRoot(liveTheme = null) {
  const root = fakeProbe('section');
  if (liveTheme) {
    root.setAttribute('data-theme', liveTheme);
  }

  return root;
}

/** Resolve `--tm-color-heat-*` per the theme switch the element itself (or an ancestor) carries. */
function themeAwareStyle(values = {}) {
  return element => {
    const switchElement = typeof element.closest === 'function' ? element.closest('[data-theme]') : null;
    const theme = switchElement ? switchElement.getAttribute('data-theme') : 'light';
    const palette = theme === 'dark'
      ? { '--tm-color-heat-0': '#14532d', '--tm-color-heat-3': '#7f1d1d' }
      : { '--tm-color-heat-0': '#dcfce7', '--tm-color-heat-3': '#dc2626' };
    return {
      getPropertyValue(name) {
        return name in values ? values[name] : (palette[name] ?? '');
      },
    };
  };
}

test('normalizeTokenNames trims, prefixes, dedupes and drops empty entries', () => {
  assert.deepEqual(
    normalizeTokenNames(['--tm-color-heat-0', 'tm-color-heat-1', ' --tm-color-heat-0 ', '', null, 7]),
    ['--tm-color-heat-0', '--tm-color-heat-1']);
  assert.deepEqual(normalizeTokenNames(null), []);
  assert.deepEqual(normalizeTokenNames('--tm-color-heat-0'), []);
});

test('liveThemeOf reads the closest switch and defaults to light', () => {
  assert.equal(liveThemeOf(fakeRoot()), 'light');
  assert.equal(liveThemeOf(fakeRoot('dark')), 'dark');
  assert.equal(liveThemeOf(fakeRoot('light')), 'light');
  assert.equal(liveThemeOf(null), 'light');

  const outer = fakeRoot('dark');
  const inner = fakeProbe('div');
  outer.appendChild(inner);
  assert.equal(liveThemeOf(inner), 'dark', 'an element without a switch inherits the closest one');

  const classOnly = fakeProbe('div');
  classOnly.classList.add('tm-dark');
  assert.equal(liveThemeOf(classOnly), 'dark', 'the .tm-dark class is the second dark switch');
});

test('resolveTokensIn reads the requested theme off a probe and never touches documentElement', () => {
  const doc = fakeDocument();
  const root = fakeRoot('dark');

  const result = resolveTokensIn(root, HEAT_TOKENS, 'light', doc, themeAwareStyle());

  assert.deepEqual(result, {
    '--tm-color-heat-0': '#dcfce7',
    '--tm-color-heat-3': '#dc2626',
  });
  assert.deepEqual(doc.mutations, [], 'the document element must stay untouched');
  assert.equal(root.children.length, 0, 'the probe is removed again');
  assert.equal(doc.created.length, 1);
  assert.equal(doc.created[0].getAttribute('data-theme'), 'light');
  assert.equal(doc.created[0].getAttribute('aria-hidden'), 'true');
});

test('resolveTokensIn adds the tm-dark class to a dark probe and removes it afterwards', () => {
  const doc = fakeDocument();
  const root = fakeRoot('light');

  const result = resolveTokensIn(root, HEAT_TOKENS, 'dark', doc, themeAwareStyle());

  assert.equal(result['--tm-color-heat-3'], '#7f1d1d');
  assert.ok(doc.created[0].classes.has('tm-dark'), 'the class switch mirrors [data-theme="dark"]');
  assert.equal(root.children.length, 0);
  assert.deepEqual(doc.mutations, []);
});

test('resolveTokensIn takes the live shortcut when the root already has the requested theme', () => {
  const doc = fakeDocument();
  const root = fakeRoot('dark');

  const result = resolveTokensIn(root, HEAT_TOKENS, 'dark', doc, themeAwareStyle());

  assert.equal(result['--tm-color-heat-3'], '#7f1d1d');
  assert.equal(doc.created.length, 0, 'a matching theme needs no probe');
  assert.deepEqual(doc.mutations, []);
});

test('resolveTokensIn without a requested theme reads the caller root', () => {
  const doc = fakeDocument();
  const root = fakeRoot('dark');
  const values = { '--tm-color-heat-1': '#fed7aa' };

  const result = resolveTokensIn(root, ['--tm-color-heat-1'], null, doc, () => ({
    getPropertyValue: name => values[name] ?? '',
  }));

  assert.deepEqual(result, { '--tm-color-heat-1': '#fed7aa' });
  assert.equal(doc.created.length, 0, 'the live read needs no probe');
  assert.deepEqual(doc.mutations, []);
});

test('resolveTokensIn omits missing/blank tokens instead of writing empty paint', () => {
  const doc = fakeDocument();
  const result = resolveTokensIn(fakeRoot(), ['--tm-color-heat-0', '--tm-color-nope'], 'light', doc, () => ({
    getPropertyValue: name => (name === '--tm-color-heat-0' ? '  #dcfce7  ' : '   '),
  }));

  assert.deepEqual(result, { '--tm-color-heat-0': '#dcfce7' });
});

test('resolveTokensIn survives a throwing getComputedStyle and still removes the probe', () => {
  const doc = fakeDocument();
  const root = fakeRoot('dark');

  const result = resolveTokensIn(root, HEAT_TOKENS, 'light', doc, () => {
    throw new Error('detached');
  });

  assert.deepEqual(result, {});
  assert.equal(root.children.length, 0, 'the finally block removes the probe');
  assert.deepEqual(doc.mutations, []);
});

test('resolveTokensIn returns an empty map without a root scope or token names', () => {
  const doc = fakeDocument();
  assert.deepEqual(resolveTokensIn(null, HEAT_TOKENS, 'light', doc, () => ({})), {});
  assert.equal(doc.created.length, 0);

  const root = fakeRoot('dark');
  assert.deepEqual(resolveTokensIn(root, [], 'light', doc, () => ({})), {});
  assert.equal(doc.created.length, 0, 'an empty request never touches the DOM');
  assert.deepEqual(doc.mutations, []);
});
