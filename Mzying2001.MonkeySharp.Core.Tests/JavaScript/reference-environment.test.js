const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const os = require('node:os');
const path = require('node:path');
const crypto = require('node:crypto');
const { verifyEnvironment, extensionFingerprint } = require('../../scripts/compatibility/ReferenceEnvironment.cjs');

test('reference pins reject changed source, identity, version and incomplete beta environments', () => {
    const directory = fs.mkdtempSync(path.join(os.tmpdir(), 'monkey-reference-'));
    try {
        const key = Buffer.from('test public key').toString('base64');
        const id = crypto.createHash('sha256').update(Buffer.from(key, 'base64')).digest('hex').slice(0, 32)
            .replace(/[0-9a-f]/g, digit => String.fromCharCode(97 + parseInt(digit, 16)));
        fs.writeFileSync(path.join(directory, 'manifest.json'), JSON.stringify({ version: '5.5.1', manifest_version: 3, key }));
        fs.writeFileSync(path.join(directory, 'background.js'), 'original');
        const pin = { channel: 'stable', version: '5.5.1', manifestVersion: 3, extensionId: id, contentSha256: extensionFingerprint(directory) };
        assert.equal(verifyEnvironment(directory, pin).contentSha256, pin.contentSha256);
        assert.throws(() => verifyEnvironment(directory, { ...pin, version: '5.6.6242' }), /version/);
        assert.throws(() => verifyEnvironment(directory, { ...pin, extensionId: 'a'.repeat(32) }), /identity/);
        assert.throws(() => verifyEnvironment(directory, { ...pin, channel: 'beta', contentSha256: null }), /explicit/);
        fs.writeFileSync(path.join(directory, 'background.js'), 'changed');
        assert.throws(() => verifyEnvironment(directory, pin), /SHA-256/);
    } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
