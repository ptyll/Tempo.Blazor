/**
 * Tempo.Blazor.PdfViewer — document byte length for the header's file-size subtitle.
 *
 * PDF.js exposes the byte length through `pdfDoc.getDownloadInfo()` — a promise that may
 * reject (a stream without a length), resolve without the field, or resolve with a hostile
 * value. `documentLength()` normalizes all of that to a whole non-negative byte count or
 * null, so pdf-viewer.js can skip the .NET round-trip entirely when the size is unknown
 * and the header keeps its pages-only subtitle. Node-tested via `npm run test:pdf-viewer`.
 */

/**
 * Reads the document byte length from a PDF.js document proxy.
 * @param {{ getDownloadInfo?: () => Promise<{ length?: number }> } | null} pdfDoc The open document.
 * @returns {Promise<number|null>} The whole byte length, or null when unknown.
 */
export async function documentLength(pdfDoc) {
    if (!pdfDoc || typeof pdfDoc.getDownloadInfo !== 'function') {
        return null;
    }

    try {
        const info = await pdfDoc.getDownloadInfo();
        const length = info?.length;
        if (typeof length !== 'number' || !Number.isFinite(length) || length < 0) {
            return null;
        }
        return Math.round(length);
    } catch {
        return null;
    }
}
