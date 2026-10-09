// Tempo.Blazor editor shell (ES module).
//
// Optional panel-width persistence for TmEditorShell. The widths are plain JSON under a
// host-chosen localStorage key; both functions are best-effort (private mode, disabled storage
// and SSR simply skip persistence).

/**
 * Reads the stored panel widths JSON, or null when nothing is stored / storage is unavailable.
 * @param {string} key the host-chosen persistence key
 * @returns {string|null} the stored JSON ("{\"left\":\"280px\",\"right\":\"320px\"}")
 */
export function loadPanelWidths(key) {
    try {
        return globalThis.localStorage?.getItem(`tempo.tm-editor-shell.${key}`) ?? null;
    }
    catch {
        return null;
    }
}

/**
 * Stores the panel widths JSON. No-op when storage is unavailable.
 * @param {string} key the host-chosen persistence key
 * @param {string} json the widths JSON
 */
export function savePanelWidths(key, json) {
    try {
        globalThis.localStorage?.setItem(`tempo.tm-editor-shell.${key}`, json);
    }
    catch {
        // Storage unavailable (private mode, quota) — persistence is best-effort only.
    }
}
