// The variable picker inserts a token at the caret of the LAST edited field. That "last field" must never
// be a search box: focusing the picker's own search (or the palette's block search) used to make the token
// land in the filter instead of the content field the user opened the picker for — the model then silently
// missed the token, and on a caret-less path the token could even be written into a search input.
//
// The rules pinned here: `type=search` is never an insertion target, and neither is anything inside
// `[data-tm-variable-picker]` / `[data-tm-toolbox-search]` (a zone marker, so a future text input inside
// either surface is excluded without another selector).
import test from 'node:test';
import assert from 'node:assert/strict';

// The module registers its focusin listener at import time, so the fake document has to exist first.
const listeners = [];
globalThis.document = {
  addEventListener(type, handler) {
    listeners.push({ type, handler });
  },
};

function fireFocusin(target) {
  for (const entry of listeners) {
    if (entry.type === 'focusin') entry.handler({ target });
  }
}

function fakeInput({ tagName = 'INPUT', type = 'text', ancestors = [] } = {}) {
  const input = {
    tagName,
    type,
    value: '',
    selectionStart: 0,
    selectionEnd: 0,
    isConnected: true,
    focused: false,
    events: [],
    ancestors,
    closest(selector) {
      const wanted = selector.split(',').map((part) => part.trim());
      return wanted.some((wantedSelector) => this.ancestors.includes(wantedSelector)) ? {} : null;
    },
    setSelectionRange(start, end) {
      this.selectionStart = start;
      this.selectionEnd = end;
    },
    dispatchEvent(event) {
      this.events.push(event);
      return true;
    },
    focus() {
      this.focused = true;
    },
  };
  return input;
}

const { insertToken } = await import('../tm-email-variable-insert.js');

test('a search input is never the insertion target', () => {
  const search = fakeInput({ type: 'search' });
  fireFocusin(search);

  assert.equal(insertToken('{{ user.email }}'), false,
    'the picker filter must not receive the token');
  assert.equal(search.value, '', 'the search field keeps its text');
});

test('inputs inside the variable picker and the toolbox search zone are ignored', () => {
  const inPicker = fakeInput({ ancestors: ['[data-tm-variable-picker]'] });
  fireFocusin(inPicker);
  assert.equal(insertToken('{{ a }}'), false, 'a field of the picker is not a content field');

  const inToolbox = fakeInput({ ancestors: ['[data-tm-toolbox-search]'] });
  fireFocusin(inToolbox);
  assert.equal(insertToken('{{ b }}'), false, 'the block search is not a content field');
});

test('a regular text field still receives the token at the caret and re-focuses', () => {
  const field = fakeInput();
  field.value = 'Ahoj svete';
  field.selectionStart = 5;
  field.selectionEnd = 10;
  fireFocusin(field);

  assert.equal(insertToken('{{ user.email }}'), true);
  assert.equal(field.value, 'Ahoj {{ user.email }}');
  assert.equal(field.selectionStart, 5 + '{{ user.email }}'.length, 'the caret follows the token');
  assert.equal(field.selectionEnd, field.selectionStart);
  assert.equal(field.events[0]?.type, 'input', "Blazor's binding learns about the write");
  assert.equal(field.focused, true);
});

test('a textarea is a valid insertion target', () => {
  const area = fakeInput({ tagName: 'TEXTAREA' });
  fireFocusin(area);

  assert.equal(insertToken('{{ x }}'), true);
  assert.equal(area.value, '{{ x }}');
});

test('the remembered field survives a focus round-trip through the picker search', () => {
  const field = fakeInput();
  fireFocusin(field);
  fireFocusin(fakeInput({ type: 'search' }));

  assert.equal(insertToken('!'), true, 'the picker search did not clear the remembered field');
  assert.equal(field.value, '!');
});

test('a detached remembered field is refused instead of written into nowhere', () => {
  const field = fakeInput();
  fireFocusin(field);
  field.isConnected = false;

  assert.equal(insertToken('{{ y }}'), false);
});
