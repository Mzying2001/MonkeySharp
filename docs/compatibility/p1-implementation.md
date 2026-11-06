# P1 implementation record

Date: 2026-11-07. Work proceeds through steps 7–14, with one validated commit per step. P0 evidence and limitations remain in [the P0 record](p0-implementation.md).

| Step | State | Evidence |
| --- | --- | --- |
| 7. Installation preparation | Complete | [Prepared revisions matrix](evidence/prepared-revisions-p1.json), [Tampermonkey 5.5.1/MV3 reference](evidence/tampermonkey-5.5.1-prepared-content.json) |
| 8. Atomic storage snapshot and sequence | Complete | [Storage preparation matrix](evidence/atomic-storage-p1.json) |
| 9. Synchronous storage and listeners | Pending | — |
| 10. Matching and metadata conditions | Pending | — |
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
