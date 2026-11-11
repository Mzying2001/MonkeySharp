# Enhanced compatibility follow-up

This tracks steps 28?33 and the step 36 delivery. [M1 gaps](m1-report.md) remain unchanged; a completed experiment is distinct from an implemented capability.

| Step | Result |
| --- | --- |
| 28 | Incremental non-fetch text/JSON/document/xml responseText, one 64 KiB pull at a time, a persistent decoder, bounded progress frequency and unchanged final parsing. [Evidence](../../scripts/compatibility/evidence/progress-follow-up-p1.json). |
| 29?33 | Pending. |
| 36 | Pending final matrix. |

Text responses also expose cumulative response. JSON/document response stays null until final parsing; binary/stream never exposes partial text. Fetch mode retains MonkeySharp?s existing suppression of progress and intermediate readyState events. Fixed 5.6.6242 actually emits fetch progress: this is a recorded intentional difference, not parity. UTF-8 decoding uses replacement for malformed bytes; unsupported charset names retain the existing UTF-8 fallback.

The signed Chrome beta CRX is pinned by package hash and publisher signature. Unpacking restores only manifest.key from the publisher proof matching the signed CRX identity; executable files are unchanged. The first acquisition selected the store proof instead of the publisher proof and the first fixture incorrectly expected no fetch progress. That failed run is retained and is not identity-qualified evidence. The corrected publisher run and reproducibly unpacked run are retained separately.

Reproduce with `node scripts/compatibility/Unpack-Reference.cjs <signed.crx> <new-directory>`, then `node scripts/compatibility/Run-Reference.cjs <Edge-exe> <new-directory> <new-output.json> scripts/compatibility/fixtures/p1-progress.user.js scripts/compatibility/reference-beta.json`. The pin rejects a newer package or changed source; Playwright is required. Native reproduction uses the existing SmokeHost Run-E2E.ps1; the binary case now includes delayed chunked text, split UTF-8, gzip and abort/timeout after partial delivery. Existing charset, redirect, response-type and binary checks remain active.

Fixed beta uploads exact stream bytes over the controlled HTTP/2 TLS endpoint; HTTP/1.1 attempts failed with Failed to fetch and remain recorded. Its reader remained locked, 307 replay rejected a disturbed stream, and disturbed/producer-error operations had no terminal outcome within 1.5 seconds. These are bounded observations, not claims that they never settle. MonkeySharp deliberately releases readers, rejects bad inputs promptly and replays staged bytes. Permission denial does not acquire a reader; a producer may independently prefill its own stream before this API receives it.

Step 30 keeps two features separate: running GM command callbacks and launching @run-at context-menu source. The latter uses captured document targets and one-shot context-private proofs on 84, so navigation cannot retarget an old menu to a new document. 121 emits its native message event asynchronously after disposing the borrowed IFrame; 151 lacks PostMessage in the tested isolated child context. Main-frame support is retained on both. No page ID, URL comparison or DOM marker substitutes for native identity. This is a scoped implementation; full step 30 acceptance is incomplete.

The beta native-menu fixture requires a pin with settings.headless=false and real native input, then its Save menu evidence button. The attempted automated run was stopped because the computer-use native pipe could not connect after retry/reset. No native selection was performed. Callback payload/WPF bridge checks and unit filters are not a beta native-click result.
