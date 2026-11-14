const test = require('node:test');
const assert = require('node:assert/strict');
const { waitFor } = require('./test-wait');

test('waits fail with a named deadline and preserve predicate failures', async () => {
    await assert.rejects(waitFor(() => false, 'bootstrap acknowledgement', 10), /bootstrap acknowledgement/);
    await assert.rejects(waitFor(() => new Promise(() => {}), 'stalled evaluation', 10), /stalled evaluation/);
    const failure = new Error('invalid bridge response');
    await assert.rejects(waitFor(() => { throw failure; }), error => error === failure);
    let attempts = 0;
    assert.equal(await waitFor(() => ++attempts === 3 && 'ready'), 'ready');
});
