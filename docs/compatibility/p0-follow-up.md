# Stable compatibility follow-up (2026-11-10)

This tracks steps 21–27. The reference is Tampermonkey 5.5.1/MV3 (Chromium 120
minimum), independently of MonkeySharp's CefSharp 84.4.10 baseline. New runs use
Edge 157.0.4341.42 and fresh profiles. Historical evidence is retained.

| Step | Scope and current result |
| --- | --- |
| 21 | Pinned identity/content and stable DOM, Cookie, URL and notification probes. Native download permission interaction and notification clicks remain pending. Beta 5.6.6242 and historical MV2 have no acquired content pin/results. |
| 22 | DOM failures return/fulfill null; body fallback added, parameter/programming errors preserved. 116 JS tests and 84/121/151 x64 SmokeHost/CDP passed; [evidence](../../scripts/compatibility/evidence/dom-follow-up-p0.json). |
| 23 | Pending implementation and native verification. |
| 24 | Pending implementation and native verification. |
| 25 | Pending download semantics and documentation reconciliation. |
| 26 | Seven third-party categories pending intake/execution. |
| 27 | M1 and original step 15 remain incomplete until corpus and matrix requirements pass. |

## Stable observations

Tracked `fixtures/p0-*.user.js` and `evidence/tampermonkey-5.5.1-p0-*.json` under
`scripts/compatibility/` contain source hashes, events and network response bodies.

- DOM: invalid tag/attribute/parent and empty tag return null; modern fulfills
  null. Non-string tags fail normalization (legacy TypeError, modern rejection).
  Default parent falls back from head to body to document element. A returned
  node does not establish a CSP effect.
- Cookie: `headers.Cookie` takes priority over the entire `details.cookie`
  string. Explicit names override jar names; repeated explicit names use the
  last value, preserving empty values and embedded `=`. With an explicit string
  the reference also collapses jar names across paths. Anonymous suppresses the
  jar but still sends explicit cookies, with headers taking priority.
- URL: plain object containing only `url`, not a DOM Event. `once` works;
  AbortSignal is ignored. MonkeySharp's `oldURL`, property handler, facade
  receiver and AbortSignal are additional guarantees, not asserted TM parity.
- Notification replacement times out with false; the first tagged Promise can
  remain pending. Native click/default-action/navigation evidence is pending.
- Download: the fresh native-mode profile opens browser interaction and has no
  terminal result within the probe; the record is **blocked**. The official
  [error categories](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_download)
  distinguish extension policy (`not_whitelisted`), permission (`not_permitted`)
  and disabled downloads (`not_enabled`). No error result is invented.

The initial URL experiment assumed AbortSignal support and failed. Its record
is retained alongside the corrected observation. Missing events do not prove
native interaction or failure handling.

## Reproduction

See [runner instructions](../../scripts/compatibility/README.md). No ignored
planning files are automation inputs. Extension fingerprints hash the sorted
`[relativePath, fileSha256]` JSON array, excluding Chromium's regenerated
`_metadata` verification cache. Symbolic links are rejected. The browser loads
a copy of source in the new test profile.

A separately acquired beta uses the fifth positional environment JSON with
exact channel, version, MV, extension ID, content fingerprint and settings.
Incomplete or mismatching pins fail before browser launch. This facility does
not establish beta feature support.
