const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');

const sha256 = bytes => crypto.createHash('sha256').update(bytes).digest('hex');
function extensionFingerprint(directory) {
    const files = [];
    function visit(relative) {
        for (const name of fs.readdirSync(path.join(directory, relative)).sort()) {
            // Chromium regenerates verification caches; these are not extension source.
            if (!relative && name === '_metadata') continue;
            const entry = path.join(relative, name);
            const stat = fs.lstatSync(path.join(directory, entry));
            if (stat.isSymbolicLink()) throw new Error('Reference extension cannot contain symbolic links.');
            if (stat.isDirectory()) visit(entry);
            else files.push([entry.replaceAll('\\', '/'), sha256(fs.readFileSync(path.join(directory, entry)))]);
        }
    }
    visit('');
    return sha256(JSON.stringify(files));
}
function verifyEnvironment(directory, pin) {
    if (!['stable', 'beta'].includes(pin.channel) || !/^\d+\.\d+\.\d+$/.test(pin.version) ||
        pin.manifestVersion !== 3 || !/^[a-p]{32}$/.test(pin.extensionId) || !/^[a-f0-9]{64}$/.test(pin.contentSha256))
        throw new Error('Reference environment needs an explicit channel, version, MV3 identity and content SHA-256.');
    const manifest = JSON.parse(fs.readFileSync(path.join(directory, 'manifest.json'), 'utf8'));
    const id = manifest.key && crypto.createHash('sha256').update(Buffer.from(manifest.key, 'base64')).digest('hex')
        .slice(0, 32).replace(/[0-9a-f]/g, digit => String.fromCharCode(97 + parseInt(digit, 16)));
    if (manifest.version !== pin.version || manifest.manifest_version !== pin.manifestVersion || id !== pin.extensionId)
        throw new Error('Reference extension version, manifest or identity does not match the pin.');
    const contentSha256 = extensionFingerprint(directory);
    if (contentSha256 !== pin.contentSha256) throw new Error('Reference extension content does not match the pinned SHA-256.');
    return { manifest, contentSha256 };
}
module.exports = { extensionFingerprint, verifyEnvironment };
