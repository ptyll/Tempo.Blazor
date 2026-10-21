// The email editor cancels the browser's own "save page" default for Ctrl/⌘+S only. A wrong chord match
// either swallows a key the user needs (a static @onkeydown:preventDefault would also swallow Tab) or lets
// the browser dialog open on top of the editor's save. Both are contract bugs, so the matching is pinned.
//
// The guard is PER ELEMENT (a WeakMap): two editors on one page must not disarm each other, and a host that
// renders a second editor must not steal the first one's chord.
import test from 'node:test';
import assert from 'node:assert/strict';

import {
  attachSaveShortcut,
  detachSaveShortcut,
  isSaveShortcutAttached,
} from '../tm-email-save-shortcut.js';

function fakeElement() {
  const listeners = [];
  return {
    listeners,
    addEventListener(type, handler, capture) {
      listeners.push({ type, handler, capture });
    },
    removeEventListener(type, handler) {
      const index = listeners.findIndex((entry) => entry.handler === handler);
      if (index >= 0) listeners.splice(index, 1);
    },
    fire(event) {
      for (const entry of [...listeners]) entry.handler(event);
    },
  };
}

function key(init) {
  return {
    ctrlKey: false,
    metaKey: false,
    altKey: false,
    shiftKey: false,
    key: '',
    code: '',
    defaultPrevented: false,
    preventDefault() {
      this.defaultPrevented = true;
    },
    ...init,
  };
}

test('Ctrl+S is cancelled so the browser never opens its Save-page dialog', () => {
  const element = fakeElement();
  assert.equal(attachSaveShortcut(element), true);
  assert.equal(isSaveShortcutAttached(element), true);

  const event = key({ ctrlKey: true, key: 's', code: 'KeyS' });
  element.fire(event);

  assert.equal(event.defaultPrevented, true);
  detachSaveShortcut(element);
  assert.equal(isSaveShortcutAttached(element), false);
});

test('Cmd+S (metaKey) is cancelled too, and so is the physical S key on another layout', () => {
  const element = fakeElement();
  attachSaveShortcut(element);

  const meta = key({ metaKey: true, key: 's' });
  element.fire(meta);
  assert.equal(meta.defaultPrevented, true);

  const shifted = key({ ctrlKey: true, key: 'S' });
  element.fire(shifted);
  assert.equal(shifted.defaultPrevented, true);

  const layout = key({ ctrlKey: true, key: 'ы', code: 'KeyS' });
  element.fire(layout);
  assert.equal(layout.defaultPrevented, true);

  detachSaveShortcut(element);
});

test('every other key keeps its default (typing S, Tab, Ctrl+Z, Alt+S)', () => {
  const element = fakeElement();
  attachSaveShortcut(element);

  for (const event of [
    key({ key: 's', code: 'KeyS' }),
    key({ key: 'Tab', code: 'Tab' }),
    key({ ctrlKey: true, key: 'z', code: 'KeyZ' }),
    key({ altKey: true, key: 's', code: 'KeyS' }),
  ]) {
    element.fire(event);
    assert.equal(event.defaultPrevented, false, `${event.key} must keep its default`);
  }

  detachSaveShortcut(element);
});

test('the listener is attached in the capture phase and never stops propagation', () => {
  const element = fakeElement();
  attachSaveShortcut(element);

  assert.equal(element.listeners.length, 1);
  assert.equal(element.listeners[0].capture, true);

  const event = key({ ctrlKey: true, key: 's', stopPropagation() { throw new Error('must not stop propagation'); } });
  element.fire(event);

  detachSaveShortcut(element);
});

test('attaching twice to the same element keeps exactly one listener', () => {
  const element = fakeElement();

  attachSaveShortcut(element);
  attachSaveShortcut(element);

  assert.equal(element.listeners.length, 1, 'a re-render must not stack listeners');
  detachSaveShortcut(element);
  assert.equal(element.listeners.length, 0);
});

test('two editors keep independent guards: detaching one leaves the other armed', () => {
  const first = fakeElement();
  const second = fakeElement();

  attachSaveShortcut(first);
  attachSaveShortcut(second);

  assert.equal(isSaveShortcutAttached(first), true);
  assert.equal(isSaveShortcutAttached(second), true);

  detachSaveShortcut(first);
  assert.equal(isSaveShortcutAttached(first), false, 'the disposed editor releases its own root');
  assert.equal(first.listeners.length, 0);

  assert.equal(isSaveShortcutAttached(second), true, 'the other editor must stay guarded');
  assert.equal(second.listeners.length, 1);

  const event = key({ ctrlKey: true, key: 's' });
  second.fire(event);
  assert.equal(event.defaultPrevented, true);

  detachSaveShortcut(second);
  assert.equal(second.listeners.length, 0);
});

test('a null element is a no-op, and detaching an unguarded element is safe', () => {
  assert.equal(attachSaveShortcut(null), false);
  assert.equal(isSaveShortcutAttached(null), false);
  const element = fakeElement();
  assert.equal(detachSaveShortcut(element), false);
  assert.equal(detachSaveShortcut(null), false);
  assert.equal(attachSaveShortcut(element), true);
  assert.equal(detachSaveShortcut(element), true);
});
