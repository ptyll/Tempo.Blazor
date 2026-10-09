// Tempo.Blazor editor shell (ES module).
//
// Panel-width resizing and optional persistence for TmEditorShell. RED stubs: the real module lands
// with the Q4 implementation commit.

export function loadPanelWidths(key) {
    try {
        return globalThis.localStorage?.getItem(`tempo.tm-editor-shell.${key}`) ?? null;
    }
    catch {
        return null;
    }
}

export function savePanelWidths(key, json) {
    try {
        globalThis.localStorage?.setItem(`tempo.tm-editor-shell.${key}`, json);
    }
    catch {
        // Storage unavailable.
    }
}

export function clampWidth() { throw new Error('not implemented'); }
export function canvasWidth() { throw new Error('not implemented'); }
export function attachResize() { throw new Error('not implemented'); }
export function detachResize() { throw new Error('not implemented'); }
export function __resetForTests() {}