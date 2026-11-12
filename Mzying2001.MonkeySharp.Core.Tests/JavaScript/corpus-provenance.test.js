const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const { validateCorpus } = require('../../scripts/compatibility/Run-Corpus.cjs');
const original = JSON.parse(fs.readFileSync(path.resolve(__dirname, '../../scripts/compatibility/corpus.json'), 'utf8'));

test('corpus verifies unmodified upstream bytes, immutable URLs and license content before browser startup', () => {
    validateCorpus(original);
    const modified = structuredClone(original);
    modified.scripts['site-asset-dl'].sha256 = '0'.repeat(64);
    assert.throws(() => validateCorpus(modified), /source bytes/);
    const floating = structuredClone(original);
    floating.scripts['find-scripts'].sourceUrl = floating.scripts['find-scripts'].sourceUrl.replace(floating.scripts['find-scripts'].commit, 'main');
    assert.throws(() => validateCorpus(floating), /immutable provenance/);
    const license = structuredClone(original);
    license.scripts['site-asset-dl'].licenseSha256 = '0'.repeat(64);
    assert.throws(() => validateCorpus(license), /license bytes/);
});
