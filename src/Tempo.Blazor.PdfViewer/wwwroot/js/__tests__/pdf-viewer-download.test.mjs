import test from 'node:test';
import assert from 'node:assert/strict';

import { DOWNLOAD_ALLOWED_PROTOCOLS, resolveDownload } from '../pdf-viewer-download.mjs';

const BASE = 'https://viewer.example.com/app/page';

test('same-origin http(s) URLs take the anchor download path', () => {
    assert.deepEqual(resolveDownload('/files/doc.pdf', BASE), {
        method: 'anchor',
        href: 'https://viewer.example.com/files/doc.pdf',
    });
    assert.deepEqual(resolveDownload('https://viewer.example.com/doc.pdf?x=1#p', BASE), {
        method: 'anchor',
        href: 'https://viewer.example.com/doc.pdf?x=1#p',
    });
});

test('relative URLs are resolved against the document base', () => {
    assert.deepEqual(resolveDownload('doc.pdf', 'https://viewer.example.com/app/'), {
        method: 'anchor',
        href: 'https://viewer.example.com/app/doc.pdf',
    });
    // Scheme-relative and root-relative forms behave like the same URL in an anchor.
    assert.equal(resolveDownload('//other.example.com/doc.pdf', BASE).href,
        'https://other.example.com/doc.pdf');
});

test('blob: URLs take the anchor path (opaque origin, rule-based)', () => {
    const blob = 'blob:https://viewer.example.com/1a2b3c';
    assert.deepEqual(resolveDownload(blob, BASE), { method: 'anchor', href: blob });
    // Even a blob minted elsewhere: it cannot be re-fetched by the server, anchor it.
    assert.equal(resolveDownload('blob:https://elsewhere.example.com/x', BASE).method, 'anchor');
});

test('cross-origin http(s) URLs open in a new tab instead of downloading', () => {
    const decision = resolveDownload('https://cdn.other.example.com/doc.pdf', BASE);
    assert.deepEqual(decision, {
        method: 'window',
        href: 'https://cdn.other.example.com/doc.pdf',
    });
});

test('non-http(s)/blob: protocols are rejected outright', () => {
    for (const url of [
        'javascript:alert(1)',
        'data:application/pdf;base64,AAAA',
        'file:///etc/passwd',
        'ftp://viewer.example.com/doc.pdf',
        'chrome://settings',
    ]) {
        assert.equal(resolveDownload(url, BASE).method, 'none', url);
    }
});

test('malformed and empty inputs are rejected without throwing', () => {
    assert.equal(resolveDownload('', BASE).method, 'none');
    assert.equal(resolveDownload('http://', BASE).method, 'none');
    assert.equal(resolveDownload(null, BASE).method, 'none');
    assert.equal(resolveDownload(undefined, BASE).method, 'none');
    // An unusable base is a hard failure (the WHATWG parser validates the base first):
    // pdf-viewer.js downloads nothing rather than guessing (document.baseURI is always
    // a valid absolute URL in the browser, so this is purely defensive).
    assert.equal(resolveDownload('https://ok.example.com/a.pdf', 'not-a-base-uri').method, 'none');
});

test('the allow-list is exactly http, https and blob', () => {
    assert.deepEqual([...DOWNLOAD_ALLOWED_PROTOCOLS].sort(), ['blob:', 'http:', 'https:']);
});
