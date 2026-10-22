import test from 'node:test';
import assert from 'node:assert/strict';

import { documentLength } from '../pdf-viewer-filesize.mjs';

test('reportDocumentLength_UsesGetDownloadInfo_FallsBackToNull', async () => {
    // A normal PDF.js download report: the byte length travels to the header subtitle.
    assert.equal(await documentLength({ getDownloadInfo: async () => ({ length: 1_500_000 }) }), 1_500_000);

    // A fractional length is rounded to whole bytes (the C# contract is long).
    assert.equal(await documentLength({ getDownloadInfo: async () => ({ length: 2048.6 }) }), 2049);

    // Unknown length (no info object): null — the header keeps the pages-only subtitle.
    assert.equal(await documentLength({ getDownloadInfo: async () => undefined }), null);
    assert.equal(await documentLength({ getDownloadInfo: async () => ({}) }), null);

    // A hostile shape (negative / non-numeric) must not reach the interop boundary.
    assert.equal(await documentLength({ getDownloadInfo: async () => ({ length: -1 }) }), null);
    assert.equal(await documentLength({ getDownloadInfo: async () => ({ length: '12' }) }), null);
    assert.equal(await documentLength({ getDownloadInfo: async () => ({ length: NaN }) }), null);

    // A rejected getDownloadInfo never throws: null, and nothing is reported.
    assert.equal(await documentLength({ getDownloadInfo: async () => { throw new Error('gone'); } }), null);

    // A missing document (null) is tolerated too.
    assert.equal(await documentLength(null), null);
});
