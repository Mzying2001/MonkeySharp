const { performance } = require('node:perf_hooks');
const { setTimeout: delay, setImmediate: nextTurn } = require('node:timers/promises');

async function waitFor(predicate, description = 'test condition', timeout = 2000) {
    const deadline = performance.now() + timeout;
    do {
        const remaining = deadline - performance.now();
        if (remaining <= 0) throw new Error(`Timed out waiting for ${description} (${timeout} ms)`);
        let timer;
        try {
            const value = await Promise.race([
                Promise.resolve().then(predicate),
                new Promise((_, reject) => { timer = setTimeout(() => reject(new Error(`Timed out waiting for ${description} (${timeout} ms)`)), remaining); })
            ]);
            if (value) return value;
        } finally { clearTimeout(timer); }
        await delay(Math.min(5, remaining));
    } while (true);
}

module.exports = { waitFor, nextTurn };
