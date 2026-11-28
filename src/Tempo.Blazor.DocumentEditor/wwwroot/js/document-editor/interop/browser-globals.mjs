// Browser-global interop surface for TmDocumentEditor.
// The component invokes these through IJSRuntime by global name
// ("tmDocumentEditor.enableBeforeUnloadGuard", "tmDocumentEditor.downloadFile"),
// and E2E tests introspect window.tmDocumentEditor.getBeforeUnloadGuardState().
// Importing this module installs the global; installation is idempotent and
// merges into any existing window.tmDocumentEditor without clobbering
// foreign members.

const GUARD_HANDLER = Symbol.for('tmDocumentEditor.beforeUnloadHandler');
const FULLSCREEN_CLASS = 'tm-document-editor--fullscreen';

export function installDocumentEditorBrowserGlobals(win = globalThis.window ?? globalThis) {
  const target = win.tmDocumentEditor ?? (win.tmDocumentEditor = {});

  target.enableBeforeUnloadGuard = function enableBeforeUnloadGuard() {
    if (this[GUARD_HANDLER]) {
      return;
    }

    this[GUARD_HANDLER] = event => {
      event.preventDefault();
      event.returnValue = '';
    };
    win.addEventListener('beforeunload', this[GUARD_HANDLER]);
  };

  target.disableBeforeUnloadGuard = function disableBeforeUnloadGuard() {
    if (!this[GUARD_HANDLER]) {
      return;
    }

    win.removeEventListener('beforeunload', this[GUARD_HANDLER]);
    this[GUARD_HANDLER] = null;
  };

  target.getBeforeUnloadGuardState = function getBeforeUnloadGuardState() {
    return { active: !!this[GUARD_HANDLER] };
  };

  function openOfflineDb() {
    return new Promise((resolve, reject) => {
      if (!win.indexedDB) {
        reject(new Error('IndexedDB is not available.'));
        return;
      }

      const request = win.indexedDB.open('tempo-blazor-document-editor', 1);
      request.onupgradeneeded = event => {
        const db = event.target.result;
        if (!db.objectStoreNames.contains('drafts')) {
          const store = db.createObjectStore('drafts', { keyPath: 'id' });
          store.createIndex('documentId', 'documentId', { unique: false });
          store.createIndex('state', 'state', { unique: false });
          store.createIndex('updatedAt', 'updatedAt', { unique: false });
        }
      };
      request.onsuccess = event => resolve(event.target.result);
      request.onerror = event => reject(event.target.error || new Error('IndexedDB request failed.'));
    });
  }

  function withDraftStore(mode, callback) {
    return openOfflineDb().then(db => new Promise((resolve, reject) => {
      const transaction = db.transaction('drafts', mode);
      const store = transaction.objectStore('drafts');
      let value;
      transaction.oncomplete = () => {
        db.close();
        resolve(value);
      };
      transaction.onerror = event => {
        db.close();
        reject(event.target.error || new Error('IndexedDB transaction failed.'));
      };
      value = callback(store);
    }));
  }

  function requestToPromise(request) {
    return new Promise((resolve, reject) => {
      request.onsuccess = event => resolve(event.target.result);
      request.onerror = event => reject(event.target.error || new Error('IndexedDB request failed.'));
    });
  }

  target.offlineStore = {
    saveDraft(draft) {
      return withDraftStore('readwrite', store => store.put(draft));
    },

    loadDraft(draftId) {
      return withDraftStore('readonly', store => requestToPromise(store.get(draftId)));
    },

    deleteDraft(draftId) {
      return withDraftStore('readwrite', store => store.delete(draftId));
    },

    listPendingDrafts(documentId) {
      return withDraftStore('readonly', store => requestToPromise(store.getAll()).then(items => (items || [])
        .filter(item => item && item.state !== 2)
        .filter(item => !documentId || item.documentId === documentId)
        .sort((left, right) => String(right.updatedAt || '').localeCompare(String(left.updatedAt || '')))));
    }
  };

  // Fullscreen is the editor's own state (data-fullscreen on its root: the bounded frame at 100dvh, lifted with
  // position:fixed above the host layout). The BODY class is kept for hosts that style around it, and the body
  // overflow lock stops the page behind the editor from scrolling.
  target.setFullscreen = function setFullscreen(fullscreen) {
    const body = win.document?.body;
    if (!body) {
      return;
    }

    if (fullscreen) {
      body.classList.add(FULLSCREEN_CLASS);
      body.style.overflow = 'hidden';
    } else {
      body.classList.remove(FULLSCREEN_CLASS);
      body.style.overflow = '';
    }
  };

  target.downloadFile = function downloadFile(fileName, contentType, base64Data) {
    const binary = win.atob(base64Data || '');
    const bytes = new Uint8Array(binary.length);
    for (let i = 0; i < binary.length; i++) {
      bytes[i] = binary.charCodeAt(i);
    }

    const BlobCtor = win.Blob;
    const blob = new BlobCtor([bytes], { type: contentType || 'application/octet-stream' });
    const url = win.URL.createObjectURL(blob);
    const anchor = win.document.createElement('a');
    anchor.href = url;
    anchor.download = fileName || 'download';
    win.document.body.appendChild(anchor);
    anchor.click();
    win.document.body.removeChild(anchor);
    // Defer the revoke: the browser needs the blob URL to complete the download, and E2E tests
    // assert the exported content by fetching it. 60 s is safely past both.
    const scheduleTimeout = typeof win.setTimeout === 'function' ? win.setTimeout.bind(win) : setTimeout;
    scheduleTimeout(() => win.URL.revokeObjectURL(url), 60_000);
  };

  return target;
}

if (typeof globalThis.window !== 'undefined') {
  installDocumentEditorBrowserGlobals(globalThis.window);
}
