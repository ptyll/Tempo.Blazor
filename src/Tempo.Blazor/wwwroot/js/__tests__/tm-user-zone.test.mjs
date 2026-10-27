import test from 'node:test';
import assert from 'node:assert/strict';
import { resolve } from '../tm-user-zone.js';

test('resolve returns an IANA zone id', () => {
    const id = resolve();
    assert.equal(typeof id, 'string');
    // IANA ids are Area/City, but UTC hosts (CI containers) legitimately report the bare "UTC"/"GMT".
    assert.match(id, /^(?:[A-Za-z_]+(?:\/[A-Za-z0-9_+\-]+)+|UTC|GMT|Etc\/.+)$/, 'IANA zone id');
});

test('resolve returns null instead of throwing when Intl is unavailable', () => {
    const original = Intl.DateTimeFormat;
    try {
        // A platform without Intl (or a locked-down browser build) must not crash the probe.
        Object.defineProperty(Intl, 'DateTimeFormat', { value: () => { throw new Error('no intl'); }, configurable: true });
        assert.equal(resolve(), null);
    } finally {
        Object.defineProperty(Intl, 'DateTimeFormat', { value: original, configurable: true });
    }
});
