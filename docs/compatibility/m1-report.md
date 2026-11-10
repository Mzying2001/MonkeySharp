# Stable follow-up delivery assessment

[简体中文](m1-report.zh-CN.md)

The code and native validation cover a **limited-scope stable delivery**. Full
M1 and original step 15 remain **incomplete**: the reference browser did not
grant downloads permission, the third-party history-refresh reference assertion
failed, and native Tampermonkey notification/download interaction comparisons
are still missing. Native test counts are not a compatibility percentage.

The evaluated implementation is `fe3c8df` (steps 21–26 plus the WPF polling and
native test-host fixes). The [final matrix evidence](../../scripts/compatibility/evidence/stable-follow-up-delivery-p0.json)
records the exact commit/patch, versions/platforms, package consumers, source
corpus, WPF checks and earlier failures. Historical `p2-delivery.json` remains a
separate dated result.

## Final native/package matrix: 2026-11-11

| CefSharp | Platform | .NET tests | JS tests | SmokeHost/CDP | WPF | Source/package corpus | NuGet/public consumer |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 84.4.10 | x64 | 413 passed | 120 passed | passed | passed | 10 + 10, declared scope | passed |
| 84.4.10 | x86 | 364 passed | 120 passed | passed | not applicable | 10 + 10, declared scope | passed |
| 121.3.70 | x64 | 413 passed | 120 passed | passed | passed | 10 + 10, declared scope | passed |
| 121.3.70 | x86 | 364 passed | 120 passed | passed | not applicable | 10 + 10, declared scope | passed |
| 151.3.240 | x64 | 413 passed | 120 passed | passed | passed | 10 + 10, declared scope | passed |
| 151.3.240 | x86 | 364 passed | 120 passed | passed | not applicable | 10 + 10, declared scope | passed |

There are no unexplained failures in the final matrix. Each 84 corpus run has
six passes and four explicit engine limits (CSP, pre-inline timing, two settings
cases); each 121/151 run has eight passes and the CSP/timing limits. All 120
source/package corpus executions retain their individual outcomes. The source
patch contains reporting-document changes only; runtime and runner inputs are
committed. Raw run: `artifacts/p2-delivery/e9c8dbd16171`.

The first matrix passed 84/121 on both architectures but failed WPF 151 during
script-disable reload; an independent repeat reproduced canceled JavaScript
polling. A minimal regression failed before the fix. The poll now retries only
TaskCanceledException within its existing 25-second deadline; direct evaluations
and assertion/programming errors still fail. All 49 Demo tests and the original
151 WPF scenario passed after the fix. Both earlier native failures are retained
in [polling evidence](../../scripts/compatibility/evidence/wpf-polling-follow-up-p0.json).

The 151/x86 corpus also had earlier no-report timeouts. The test host now waits
for Cookie storage's native readiness callback, disposes the temporary manager,
bounds evaluation and records the last phase and process termination details.
Eight independent probes and a full ten-scenario 151/x86 run passed afterward.
Earlier timeouts and an invalidated overlapping local diagnostic build remain
in [readiness evidence](../../scripts/compatibility/evidence/corpus-host-readiness-p0.json).
These corrections do not prove every underlying native timeout cause.

## Contract changes

- DOM creation/attribute/insertion failures return null; modern fulfills null.
  Normalization/programming errors remain visible. Parent fallback is
  head → body → documentElement; CSP effects remain separate.
- Explicit Cookie strings never reached the server through native URLRequest
  on 84/121/151. Default HTTP/download transport now rejects those inputs before
  dispatch. Actual-jar authentication, anonymous suppression, redirects and
  RequestContext isolation retain native checks. This is a disclosed engine
  limitation, not implemented TM merge parity.
- URL listeners support once, signals, capture identity, dispatch mutations,
  exception isolation, deduplication and cleanup. They retain plain payloads;
  oldURL/property handlers/facade receiver/signal are documented extra guarantees.
- Download permission, unsupported-backend and expired-execution errors report
  their cause. Success still follows file commit; callbacks precede settlement.
  Known adapter subfeatures use the existing compatibility map.

## API review: 37 functional rows

“Validated scope” always inherits world, grant, host-service and version limits.
The [contract table](api-contract.md) defines each calling form; original probes
and native gates are distinct from the new third-party corpus.

| # | API family, legacy / modern | Assessed scope and remaining boundary |
| --- | --- | --- |
| 1 | GM_info / GM.info | Actual identity, metadata, world and known host/subfeature facts; unknown facts omitted |
| 2 | GM_log / GM.log | Local console arguments/cycles; bounded best-effort host sink differs from extension serialization |
| 3 | getValue | Prepared complete mirror; pending remote updates need delivery |
| 4 | setValue | Legacy local update, modern host confirmation; exact TM microtask timing differs |
| 5 | deleteValue | Ordered mutations and recovery; legacy void is not disk confirmation |
| 6 | listValues | Current mirror enumeration |
| 7 | getValues | Keys/default-object/omitted overloads; mirror budgets apply |
| 8 | setValues | Atomic host batch is a MonkeySharp guarantee |
| 9 | deleteValues | Ordered atomic batch and notifications |
| 10 | addValueChangeListener | Local/remote, ownership and replay; bounded history is not a permanent message queue |
| 11 | removeValueChangeListener | Exact owned subscription cleanup |
| 12 | getResourceText | Prepared original bytes/charset/SRI; all TM fetch paths not proved equivalent |
| 13 | GM_getResourceURL / GM.getResourceUrl | Prepared data URL; URL scheme/CSP differences remain |
| 14 | addStyle | Live style node; page CSP can block effect |
| 15 | addElement | Live node/null and both overloads; CSP bypass not claimed |
| 16 | xmlhttpRequest / xmlHttpRequest | Real jar login, binary/streams/redirect/cancel; explicit Cookie and request partition rejected; preview progress/stream-body absent; proxy Firefox-only |
| 17 | download | File commit/hash, terminal cause, cancel/navigation; TM saved-file interaction comparison blocked |
| 18 | registerMenuCommand | IDs/options/native host callbacks/frame merge; clickedFrame, full native event fields and trusted DOM activation unavailable |
| 19 | unregisterMenuCommand | Owned subscription removal, merged siblings preserved |
| 20 | notification | Host click/tag/default-prevention/lifecycle checked; complete fixed TM click/tag/navigation parity pending |
| 21 | setClipboard | Actual text/HTML STA writes; other MIME types absent |
| 22 | openInTab | Modern Promise, legacy handle; numeric insert and real private context; early close queue is an extra guarantee |
| 23 | getTab | Stable host-tab/script identity |
| 24 | saveTab | Snapshot and ordered persistence across navigation |
| 25 | getTabs | Same-script host tabs, privacy isolation; not browser-wide enumeration |
| 26 | cookie.list | Actual RequestContext/filtering; versioned partition scope |
| 27 | cookie.set | Engine acceptance/readback; partition CRUD only on 121/151 |
| 28 | cookie.delete | Complete identity, sibling preservation; deliberate difference from fixed TM partition deletion |
| 29 | webRequest | Native historical-rule scope; historical MV2 parity pending, WebSocket/OOPIF/isolated dynamic interception unavailable |
| 30 | audio.setMute | Actual readback; pinned setMute returns state; 151 navigation can reset mute |
| 31 | audio.getState | Real mute, reliable audible source unavailable |
| 32 | audio.addStateChangeListener | Owned mute listeners; 200 ms polling may miss short changes; audible absent |
| 33 | audio.removeStateChangeListener | Exact callback identity and cleanup |
| 34 | unsafeWindow | Raw/page access under host trust; incompatible with DOM isolation |
| 35 | window.close | Grant/host policy; last-tab refusal is shared with TM |
| 36 | window.focus | Grant/host/OS activation policy |
| 37 | window.onurlchange | Plain object, actual history/hash changes, listener options/cleanup; additional guarantees documented |

## Corpus and acceptance

The [corpus](../../scripts/compatibility/corpus/README.md) has immutable licensed
sources and independent functional scenarios for all seven categories. Native
results include actual requests and saved-file/ZIP-entry hashes. On 84, the
unchanged Find Scripts settings UI fails on missing replaceAll; no polyfill or
source rewrite hides that limitation. CSP and pre-inline hook timing are separate
limited results. OOPIF/BFCache are not inferred from same-process iframe/SPA.

The [reference record](../../scripts/compatibility/evidence/corpus-reference-stable-p0.json)
preserves the history-refresh failure and blocked saved-file/archive/cancel
cases. The [follow-up record](../../scripts/compatibility/evidence/corpus-follow-up-p0.json)
retains the package pilot and initial byte-provenance failures. A clean checkout
needs no local notes or private script to run these gates.

Full M1 requires the unresolved fixed-reference interactions and complete
same-scenario corpus evidence. Historical MV2, preview APIs, audible and request
partitions are disclosed separate limits, not new prerequisites. Reproduce with
[validation commands](validation.md) and preserve every failed/blocked attempt.
