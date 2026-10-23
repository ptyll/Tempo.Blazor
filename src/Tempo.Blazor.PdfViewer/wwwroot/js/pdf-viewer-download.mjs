/**
 * Tempo.Blazor.PdfViewer — pure download decision (single source of truth).
 *
 * A programmatic download (the mobile "More" menu's Download entry) can no longer rely on the
 * anchor `download` attribute: browsers ignore it cross-origin, so a hostile URL would be
 * *navigated* instead of downloaded. resolveDownload() classifies a URL once, here, node-tested
 * via `npm run test:pdf-viewer`; pdf-viewer.js only executes the decision.
 *
 * Rules:
 *  - the URL is resolved against the document base (`new URL(url, document.baseURI)`), so
 *    scheme-relative and root-relative values behave like the same URL in an anchor;
 *  - only `http:`, `https:` and `blob:` are allowed — anything else (javascript:, data:,
 *    file:, …) is rejected outright;
 *  - same-origin http(s) and blob: URLs use the anchor + `download` path;
 *  - any other origin opens the resolved URL in a new tab (noopener,noreferrer).
 */

/** Protocols a programmatic download may ever touch. */
export const DOWNLOAD_ALLOWED_PROTOCOLS = ['http:', 'https:', 'blob:'];

/** Rejected decision: pdf-viewer.js performs no navigation at all. */
export const DOWNLOAD_NONE = Object.freeze({ method: 'none', href: '' });

/**
 * Decides how a URL is downloaded.
 * @param {string} url The download URL (absolute, or resolved against `baseURI`).
 * @param {string} baseURI The document base (`document.baseURI` in the browser).
 * @returns {{method: 'anchor'|'window'|'none', href: string}} The decision; `href` is the
 *   resolved absolute URL to hand to the anchor or to `window.open`.
 */
export function resolveDownload(url, baseURI) {
    if (typeof url !== 'string' || url.length === 0) {
        return DOWNLOAD_NONE;
    }

    let parsed;
    try {
        parsed = new URL(url, baseURI);
    } catch {
        return DOWNLOAD_NONE;
    }

    if (!DOWNLOAD_ALLOWED_PROTOCOLS.includes(parsed.protocol)) {
        return DOWNLOAD_NONE;
    }

    // blob: URLs have an opaque origin that never compares equal to the document's,
    // so they are anchor downloads by rule, not by the origin check.
    if (parsed.protocol === 'blob:') {
        return { method: 'anchor', href: parsed.href };
    }

    let baseOrigin = null;
    try {
        baseOrigin = new URL(baseURI).origin;
    } catch {
        baseOrigin = null;
    }

    if (baseOrigin !== null && parsed.origin === baseOrigin) {
        return { method: 'anchor', href: parsed.href };
    }

    // Cross-origin: the download attribute is not honored, so open a new tab instead —
    // the browser's own PDF viewer handles the rest and no document leaves the page context.
    return { method: 'window', href: parsed.href };
}
