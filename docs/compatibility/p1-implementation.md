# P1 implementation record

Date: 2026-11-07. Work proceeds through steps 7–14, with one validated commit per step. P0 evidence and limitations remain in [the P0 record](p0-implementation.md).

| Step | State | Evidence |
| --- | --- | --- |
| 7. Installation preparation | Complete | [Prepared revisions matrix](evidence/prepared-revisions-p1.json), [Tampermonkey 5.5.1/MV3 reference](evidence/tampermonkey-5.5.1-prepared-content.json) |
| 8. Atomic storage snapshot and sequence | Complete | [Storage preparation matrix](evidence/atomic-storage-p1.json) |
| 9. Synchronous storage and listeners | Complete | [Storage timing reference](evidence/tampermonkey-5.5.1-storage-timing.json), [Mirror validation](evidence/storage-mirror-p1.json) |
| 10. Matching and metadata conditions | Complete | [URL reference](evidence/tampermonkey-5.5.1-matching-conditions.json), [Execution conditions matrix](evidence/execution-conditions-p1.json) |
| 11. Execution worlds and lifecycle | Pending | — |
| 12. Authorization prompts | Pending | — |
| 13. XHR and binary downloads | Pending | — |
| 14. DOM, log, tabs and windows | Pending | — |

## Step 7

Repositories now activate only completely prepared revisions. Installation/edit/update fetch and validate dependencies and resources before publication. In-memory updates compare the captured revision again after preparation; durable updates serialize preparation and atomically commit source plus prepared payload. Failed preparation leaves the old revision available to navigation. Executions retain the installation they captured and never call installation providers.

Prepared content preserves original bytes, MIME, charset, dependency order and shared scope. Newline/semicolon boundaries protect against trailing comments and adjacent expressions; generated source includes dependency markers and a source URL. Resource bytes are defensively copied, and persisted payloads validate their format, source binding and SHA-256 content digests on reload. Demo format-2 databases receive an additive table; existing values and identities are retained. Old script records are prepared once during repository initialization; failures leave the database intact and stop initialization explicitly.

All five [documented SRI algorithms](https://www.tampermonkey.net/documentation.php?locale=en&q=sri) share one verifier: MD5, SHA-1, SHA-256, SHA-384 and SHA-512. Hex/Base64 and the last supported declaration are retained. The fixed TM 5.5.1/MV3 reference confirms the additional algorithms, selection order, shared scope, strict semantics and original binary data URL. Core always checks the original bytes. Text decoding deliberately follows the host-provided representation, declared charset, then UTF-8. Reference probes showed that non-UTF-8 decoding can vary when the same URL is also a resource; the committed probe records its actual combination. Universal decoding equivalence across TM fetch paths is not claimed.

The default installation content budget is 32 MiB, and the serialized bootstrap budget is 64 MiB. Resource packages larger than one bridge response use indexed chunks and complete reassembly before user code. Missing, duplicate or truncated chunks prevent execution; total-budget failures report an explicit preparation error. The package is constructed once per immutable revision and shared across frames.

Validation: 244 Core + 42 adapter + 39 Demo tests passed at both 84.4.10/x64 and 121.3.70/x64; 90 JS tests passed. All six SmokeHost/CDP combinations passed at 84.4.10, 121.3.70 and 151.3.240 × x64/x86. The new Chromium scenario verifies a resource exceeding one response, synchronous reads on the first user-code statement, binary bytes, Windows-1252 decoding, inherited strict semantics and repeated main/child-frame execution after the provider becomes unavailable. WPF Demo E2E passed at 84/121 x64, including installation resources and dependencies. SQLite offline restart is independently covered by a real persistence test. Smoke/CDP result-validator positive/negative cases passed.

Strict full-GM document-start, isolated bridge transport and the remaining P1 steps are not credited to this change. See [CDP findings](cdp-findings.md).

## Step 8

`IUserScriptValueStore` now requires atomic snapshots with a per-script watermark, contiguous replay after that watermark and atomic batch mutations with authenticated provenance. The optional snapshot interface and key-by-key fallback were removed. Gateway subscription precedes snapshot capture; the renderer assembles the pinned snapshot, replays its suffix and drains queued notifications before user code. Storage transfer IDs prevent mixing snapshots across refreshes. Duplicates are ignored, gaps trigger replay, and expired/oversized replay requests a fresh complete snapshot. Incomplete snapshots fail execution without removing granted APIs.

Memory and SQLite validate entire batches before effects and publish events only after complete commit. SQLite uses one transaction, preserves the existing value encoding/database rows and rolls back all keys on database failure. The old gateway queue that guessed a mutation's origin from its key was removed; the store carries mutation ID, execution ID, method and sequence directly. Sequences are per script and per live store session, so activity in another script cannot create false gaps. The bounded journal retains 4096 changes per partition; host restart expires execution credentials and starts a new snapshot session.

Validation: 248 Core + 42 adapter + 40 Demo tests at 84/121 x64; 96 JS tests. The original bulk failure assertions were corrected to the explicit MonkeySharp atomic store contract, which is not presented as a Tampermonkey transaction guarantee. Tests cover whole-batch rollback and provenance, snapshot/write concurrency, independent script sequences, retention/future-watermark gaps, pinned chunks during writes, duplicate/missing/truncated chunks, queued events during preparation and fresh snapshots after replay loss. All six Chromium/CDP combinations and 84/121 x64 WPF passed; the prepared-content browser case now includes a 600,000-character storage value read synchronously in both main and child frames. Step 9 still owns confirmed/pending mirror behavior and full listener semantics.

## Step 9

The renderer now keeps confirmed values separately from ordered pending operations and rebuilds the visible mirror from both. Committed events or replay update confirmed state; an exact commit watermark (including no-op batches) ensures the modern write Promise waits for acknowledgement/replay. Failed operations are removed and remaining operations are replayed, so an old rollback cannot replace a newer local or remote value. Encoding, default-object identity, undefined existence, NaN/Infinity, bulk null-prototype results and JavaScript key order are preserved. Runtime-only no-snapshot read/write fallbacks were removed.

The original timing fixture was run against unmodified Tampermonkey 5.5.1/MV3 and then unchanged through the MonkeySharp facade. Ordered events agree: local listeners run before either setter returns, followed by the recorded microtask and Promise settlement. Same-value writes and missing deletes do not notify. Local callback intent is delivered synchronously; later persistence failure reconciles the mirror and reports diagnostics without compensating callbacks. Modern host-commit rejection remains an explicit MonkeySharp boundary; identical IPC latency is not claimed. Remote changes replay in order and notify once, while local commit echoes are suppressed. Expired or untransferable event history triggers a new snapshot and `MSC414_STORAGE_REPLAY_RESET`; values recover, but unavailable historical listener events cannot be invented.

Listener registration/removal takes local effect immediately, including writes made before asynchronous registration finishes. Callback reentrancy preserves commit order, and callback exceptions do not stop other listeners. Authenticated execution-end notifications clear state; native detach sends document-scoped cleanup before removing the bridge, and stale document IDs cannot revoke another document's execution.

Validation: 248 Core + 43 adapter + 40 Demo tests at 84/121 x64; 103 JS tests. New coverage includes the original reference fixture, consecutive write failures interleaved with a remote write, callback reentrancy/exceptions, registration/removal races, replay of lost remote notifications and execution/document cleanup. All six SmokeHost/CDP combinations and 84/121 x64 WPF passed. The real frame fixture asserts that the local undefined-value listener fires before the modern setter returns; existing cross-frame/tab remote flags and navigation persistence continue to pass.

## Step 10

Prepared revisions now hold immutable URL/context rules. Matching reuses metadata-lifetime caches instead of recompiling on every frame or accumulating an unbounded string-key regex cache. The fixed TM 5.5.1/MV3 probe covers 21 URLs: include/regex case folding, literal question marks, bounded scheme/host wildcards, exclusions, query/hash handling, escaped unreserved characters, explicit/wildcard ports, IPv6 and IDN hosts. `@match` paths remain case-sensitive. Browser-serialized percent escapes are preserved while dot segments are normalized.

The probe found that Unicode host text in `@match` does not trigger injection, while Punycode rules do. Declarations are retained with `MSM084_UNSUPPORTED_IDN_MATCH`; the matcher preserves that target behavior and tells authors to use Punycode. The former tests asserting Unicode-pattern normalization were corrected. Independent probes confirm no automatic injection for missing selectors or `<all_urls>`; MonkeySharp explicitly diagnoses the latter during installation.

URL regexes accept the documented ECMAScript-compatible subset. Unsupported flags, lookbehind/named groups, backreferences, Unicode-property escapes, class subtraction and .NET-only constructs fail installation. A 100 ms timeout rejects the script even when the timed-out rule is an exclusion, records `MSR214_URL_RULE_TIMEOUT` and allows other scripts to proceed. Context-menu selection ignores include/exclude while retaining explicit match/exclude-match, noframes and privacy/trust checks, following the [documented manual-trigger contract](https://www.tampermonkey.net/documentation.php?locale=en&q=run_at).

Repeatable `@run-in` selectors use OR semantics and the real host privacy context for both automatic/manual execution. Unknown context cannot satisfy a single normal/private restriction; both supported selectors cover either context. Unknown syntax is an installation error; container selectors are diagnosed and unavailable rather than executed in another context. Demo details reflect implemented normal/private filtering. Private-tab creation remains step 18, outside this change.

Validation: 273 Core + 43 adapter + 40 Demo tests at 84/121 x64, 103 JS tests, seven browser compatibility cases in all six SmokeHost/CDP combinations, and 84/121 x64 WPF. The new real-Chromium case proves normal-context execution, private-condition rejection, include/exclude behavior and actual DOM effects of an explicitly selected context-menu script. Core tests compare the prepared rules directly against the complete reference URL matrix and cover timeout isolation, privacy unions/unknown contexts, diagnostics, IPv6/port canonicalization and immutable rule reuse.
