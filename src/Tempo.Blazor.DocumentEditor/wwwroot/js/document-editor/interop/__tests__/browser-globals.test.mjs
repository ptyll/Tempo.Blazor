import test from 'node:test';
import assert from 'node:assert/strict';
import { installDocumentEditorBrowserGlobals } from '../browser-globals.mjs';

function createFakeWindow() {
  const listeners = new Map();
  const anchors = [];
  const revoked = [];
  const win = {
    addEventListener(type, handler) {
      listeners.set(type, [...(listeners.get(type) ?? []), handler]);
    },
    removeEventListener(type, handler) {
      listeners.set(type, (listeners.get(type) ?? []).filter(h => h !== handler));
    },
    atob(data) {
      return Buffer.from(data, 'base64').toString('binary');
    },
    Blob: class FakeBlob {
      constructor(parts, options) {
        this.parts = parts;
        this.type = options?.type ?? '';
      }
    },
    URL: {
      createObjectURL(blob) {
        return `blob:fake/${blob.type || 'octet'}`;
      },
      revokeObjectURL(url) {
        revoked.push(url);
      }
    },
    document: {
      createElement(tag) {
        const el = { tag, clicked: false, click() { this.clicked = true; } };
        anchors.push(el);
        return el;
      },
      body: {
        appendChild() {},
        removeChild() {},
        classList: {
          entries: new Set(),
          add(name) { this.entries.add(name); },
          remove(name) { this.entries.delete(name); },
          contains(name) { return this.entries.has(name); }
        },
        style: { overflow: '' }
      }
    },
    scheduledDelays: [],
    setTimeout(fn, delay) { win.scheduledDelays.push(delay); fn(); return 0; }
  };
  return { win, listeners, anchors, revoked };
}

test('install creates window.tmDocumentEditor with the interop surface', () => {
  const { win } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);

  assert.ok(win.tmDocumentEditor, 'global must be created');
  for (const fn of ['enableBeforeUnloadGuard', 'disableBeforeUnloadGuard', 'getBeforeUnloadGuardState', 'downloadFile', 'setFullscreen']) {
    assert.equal(typeof win.tmDocumentEditor[fn], 'function', `${fn} must be a function`);
  }
});

test('setFullscreen toggles the body class and scroll lock', () => {
  const { win } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);
  const body = win.document.body;

  win.tmDocumentEditor.setFullscreen(true);
  assert.equal(body.classList.contains('tm-document-editor--fullscreen'), true, 'entering fullscreen must add the body class hosts key off');
  assert.equal(body.style.overflow, 'hidden', 'entering fullscreen must lock body scrolling');

  // Idempotent: re-entering keeps a single class and the lock.
  win.tmDocumentEditor.setFullscreen(true);
  assert.equal(body.classList.contains('tm-document-editor--fullscreen'), true);

  win.tmDocumentEditor.setFullscreen(false);
  assert.equal(body.classList.contains('tm-document-editor--fullscreen'), false, 'exiting fullscreen must remove the body class');
  assert.equal(body.style.overflow, '', 'exiting fullscreen must restore body scrolling');

  // Exiting when not fullscreen is a no-op.
  win.tmDocumentEditor.setFullscreen(false);
  assert.equal(body.classList.contains('tm-document-editor--fullscreen'), false);
});

test('before-unload guard toggles state and window listeners', () => {
  const { win, listeners } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);
  const api = win.tmDocumentEditor;

  assert.equal(api.getBeforeUnloadGuardState().active, false);

  api.enableBeforeUnloadGuard();
  assert.equal(api.getBeforeUnloadGuardState().active, true);
  assert.equal((listeners.get('beforeunload') ?? []).length, 1);

  // Idempotent: enabling again must not stack a second listener.
  api.enableBeforeUnloadGuard();
  assert.equal((listeners.get('beforeunload') ?? []).length, 1);

  api.disableBeforeUnloadGuard();
  assert.equal(api.getBeforeUnloadGuardState().active, false);
  assert.equal((listeners.get('beforeunload') ?? []).length, 0);

  // Disabling when inactive is a no-op.
  api.disableBeforeUnloadGuard();
  assert.equal(api.getBeforeUnloadGuardState().active, false);
});

test('the guard handler blocks unload the browser way (preventDefault + returnValue)', () => {
  const { win, listeners } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);
  win.tmDocumentEditor.enableBeforeUnloadGuard();

  const handler = listeners.get('beforeunload')[0];
  const event = { defaultPrevented: false, returnValue: undefined, preventDefault() { this.defaultPrevented = true; } };
  handler(event);

  assert.equal(event.defaultPrevented, true);
  assert.equal(event.returnValue, '');
});

test('install merges into an existing global without clobbering foreign members', () => {
  const { win } = createFakeWindow();
  win.tmDocumentEditor = { customExisting: () => 42 };
  installDocumentEditorBrowserGlobals(win);

  assert.equal(win.tmDocumentEditor.customExisting(), 42, 'existing member must survive');
  assert.equal(typeof win.tmDocumentEditor.enableBeforeUnloadGuard, 'function');

  // Idempotent double install keeps guard state working.
  installDocumentEditorBrowserGlobals(win);
  win.tmDocumentEditor.enableBeforeUnloadGuard();
  assert.equal(win.tmDocumentEditor.getBeforeUnloadGuardState().active, true);
});

test('install provides the offlineStore surface used by IndexedDbDocumentOfflineStore', () => {
  const { win } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);

  const store = win.tmDocumentEditor.offlineStore;
  assert.ok(store, 'offlineStore must exist');
  for (const fn of ['saveDraft', 'loadDraft', 'deleteDraft', 'listPendingDrafts']) {
    assert.equal(typeof store[fn], 'function', `offlineStore.${fn} must be a function`);
  }
});

test('offlineStore rejects gracefully when IndexedDB is unavailable', async () => {
  const { win } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);

  await assert.rejects(
    () => win.tmDocumentEditor.offlineStore.saveDraft({ id: 'draft-1', documentId: 'doc-1' }),
    /IndexedDB/,
    'without win.indexedDB the promise must reject so the C# safe wrapper can fall back');
});

test('downloadFile decodes base64, clicks a temporary anchor and revokes the blob URL', () => {
  const { win, anchors, revoked } = createFakeWindow();
  installDocumentEditorBrowserGlobals(win);

  const payload = Buffer.from('hello world').toString('base64');
  win.tmDocumentEditor.downloadFile('hello.txt', 'text/plain', payload);

  assert.equal(anchors.length, 1, 'one anchor must be created');
  assert.equal(anchors[0].download, 'hello.txt');
  assert.equal(anchors[0].clicked, true, 'anchor must be clicked');
  assert.ok(anchors[0].href.startsWith('blob:fake/'), 'anchor must point at the blob URL');
  assert.deepEqual(revoked, [anchors[0].href], 'blob URL must eventually be revoked');
  // The revoke must be DEFERRED (≥ 60 s): the browser download and E2E content assertions read
  // the blob URL after the click; an immediate revoke breaks both.
  assert.equal(win.scheduledDelays.length, 1, 'revoke must be scheduled, not synchronous');
  assert.ok(win.scheduledDelays[0] >= 60_000, `revoke delay must be >= 60s (got ${win.scheduledDelays[0]})`);
});
