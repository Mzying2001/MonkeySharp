// Restore the publisher identity lost when unpacking a signed CRX3. No executable source is changed.
const fs = require('node:fs');
const path = require('node:path');
const crypto = require('node:crypto');
const { execFileSync } = require('node:child_process');
const [input, directory] = process.argv.slice(2);
if (!input || !directory || fs.existsSync(directory)) throw new Error('Usage: node Unpack-Reference.cjs <signed.crx> <new-directory>');
const bytes = fs.readFileSync(input);
if (bytes.toString('ascii', 0, 4) !== 'Cr24' || bytes.readUInt32LE(4) !== 3) throw new Error('Expected CRX3');
function fields(buffer) {
    let offset = 0;
    function variable() {
        let value = 0, shift = 0, byte;
        do { if (offset >= buffer.length || shift > 28) throw new Error('Invalid protobuf');
            byte = buffer[offset++]; value += (byte & 127) * 2 ** shift; shift += 7;
        } while (byte & 128);
        return value;
    }
    const result = [];
    while (offset < buffer.length) {
        const tag = variable();
        if ((tag & 7) !== 2) throw new Error('Unexpected CRX3 wire type');
        const size = variable();
        if (offset + size > buffer.length) throw new Error('Truncated protobuf');
        result.push([tag >> 3, buffer.subarray(offset, offset + size)]); offset += size;
    }
    return result;
}
const size = bytes.readUInt32LE(8), header = fields(bytes.subarray(12, 12 + size));
const signed = header.find(([key]) => key === 10000)?.[1];
if (!signed) throw new Error('Missing signed header');
const identity = fields(signed).find(([key]) => key === 1)?.[1];
const proof = header.filter(([key]) => key === 2).map(([, value]) => Object.fromEntries(fields(value)))
    .find(value => crypto.createHash('sha256').update(value[1]).digest().subarray(0, 16).equals(identity));
if (!proof) throw new Error('No publisher proof matching signed identity');
const length = Buffer.alloc(4); length.writeUInt32LE(signed.length);
const payload = Buffer.concat([Buffer.from('CRX3 SignedData\0'), length, signed, bytes.subarray(12 + size)]);
if (!crypto.verify('sha256', payload, { key: proof[1], type: 'spki', format: 'der' }, proof[2])) throw new Error('Invalid CRX3 signature');
const zip = path.resolve(directory + '.zip');
if (fs.existsSync(zip)) throw new Error('ZIP output exists');
fs.mkdirSync(path.dirname(path.resolve(directory)), { recursive: true });
fs.writeFileSync(zip, bytes.subarray(12 + size));
execFileSync('powershell', ['-NoProfile', '-Command',
    'Add-Type -AssemblyName System.IO.Compression.FileSystem; [IO.Compression.ZipFile]::ExtractToDirectory($env:MONKEY_REFERENCE_ZIP, $env:MONKEY_REFERENCE_DEST)'],
    { env: { ...process.env, MONKEY_REFERENCE_ZIP: zip, MONKEY_REFERENCE_DEST: path.resolve(directory) } });
const file = path.join(directory, 'manifest.json'), manifest = JSON.parse(fs.readFileSync(file, 'utf8'));
manifest.key = proof[1].toString('base64');
fs.writeFileSync(file, JSON.stringify(manifest, null, 2) + '\n');
fs.unlinkSync(zip);
console.log(JSON.stringify({ packageSha256: crypto.createHash('sha256').update(bytes).digest('hex'),
    extensionId: identity.toString('hex').replace(/[0-9a-f]/g, digit => String.fromCharCode(97 + parseInt(digit, 16))), version: manifest.version }));
