# Compatibility

This index describes maintained capabilities and evidence gaps. The [validation guide](validation.md#latest-recorded-validation) records the latest native/package matrix, and the [evidence index](../../scripts/compatibility/evidence/README.md) separates reference baselines, capability matrices and historical results.

MonkeySharp targets the observable Chromium Tampermonkey contract while keeping CefSharp **84.4.10** as the minimum engine. The regression matrix also covers **121.3.70** and **151.3.240**, each on x64 and x86. The WPF Demo supports x64 only. Build the adapter against the exact CefSharp version shipped by the host.

The [API contract](api-contract.md) distinguishes legacy and modern calling forms. The [userscript guide](../usage-guide.md) / [中文](../usage-guide.zh-CN.md) owns parameters and examples; the [integration reference](../integration-reference.md) / [中文](../integration-reference.zh-CN.md) owns host interfaces and configuration.

| Topic | Maintained reference | Versioned evidence |
| --- | --- | --- |
| Grants and return shapes | [API contract](api-contract.md) | [Pinned TM 5.5.1 returns](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) and [grant probes](../../scripts/compatibility/evidence/) |
| Worlds, lifecycle and CSP | [Execution worlds](execution-worlds.md) | [Native world matrix](../../scripts/compatibility/evidence/execution-worlds.json) |
| Cookies and partitions | [Cookies](cookies.md) | [Basic Cookie matrix](../../scripts/compatibility/evidence/cookies.json), [partition matrix](../../scripts/compatibility/evidence/partition-cookies.json) |
| HTTP requests and streams | [Network scope](network.md) | [Incremental text](../../scripts/compatibility/evidence/progress-follow-up.json), [stream inputs](../../scripts/compatibility/evidence/stream-body-follow-up.json) |
| Downloads and binary inputs | [Downloads](downloads.md) | [Download matrix](../../scripts/compatibility/evidence/downloads.json), [binary matrix](../../scripts/compatibility/evidence/binary-transport.json) |
| Notifications, menus and clipboard | [Host interactions](host-interactions.md) | [Native/WPF interactions](../../scripts/compatibility/evidence/interactions.json) |
| Reproduction and delivery | [Validation](validation.md) | [Latest recorded delivery](../../scripts/compatibility/evidence/enhanced-follow-up-delivery.json) |

## Storage and resources

Installation prepares immutable source, dependencies and original resource bytes before atomic activation. Failed preparation leaves an existing revision active. Execution consumes complete prepared resources and an atomic storage snapshot before starting user code. Resource text follows host text, declared charset, then UTF-8; the pinned reference does not establish equivalent decoding across every Tampermonkey fetch path.

Storage uses per-script watermarks, bounded replay and a confirmed mirror plus ordered pending writes. Local listeners run synchronously, local echoes are suppressed, and modern writes wait for host commit/replay confirmation. Lost history can recover values through a fresh snapshot but cannot reconstruct unavailable listener events. Atomic batches are a MonkeySharp store guarantee, not a claimed Tampermonkey transaction guarantee.

See [resource preparation](../integration-reference.md#resource-integrity-and-update-checks), [storage synchronization](../integration-reference.md#storage-snapshots-and-listeners), [prepared-content evidence](../../scripts/compatibility/evidence/prepared-revisions.json), [atomic snapshots](../../scripts/compatibility/evidence/atomic-storage.json) and [mirror evidence](../../scripts/compatibility/evidence/storage-mirror.json).

## Matching and permissions

The default HTTP backend rejects explicit `cookie` and `headers.Cookie` before sending; native URLRequest ignored their requested values on 84/121/151. Ordinary jar-based authentication is retained. See [Cookie scope](cookies.md#explicit-request-cookies).

Prepared URL/run-in rules are reused across frames. URL regular expressions use a bounded ECMAScript subset; unsupported syntax fails installation and timeouts fail closed. Unicode `@match` host declarations are diagnosed; use Punycode. Manual context-menu execution ignores include/exclude while retaining match/exclude-match, frame, privacy and trust checks.

Permission decisions combine declarations, explicit host policy, remembered origin scopes and asynchronous prompts. Allow-once applies to the current joined requests. Navigation cancels obsolete waits, and redirect authorization pauses the active transfer timeout. Consent cannot widen script declarations. See [execution conditions](../integration-reference.md#prepared-execution-conditions), [interactive authorization](../integration-reference.md#interactive-authorization), [matching evidence](../../scripts/compatibility/evidence/execution-conditions.json) and [permission evidence](../../scripts/compatibility/evidence/permissions.json).

## Page and tab services

DOM helpers keep live nodes in the renderer and obey page CSP. Logs preserve original local objects, including circular objects; this differs from the pinned reference's extension serialization. Tab state belongs to a stable host tab and script, persists across navigation, and is cleared on close. Queued early tab closure is a deliberate implementation guarantee; the reference needed a later retry. Window focus/close follows host policy, and the Demo refuses to close its last tab.

See [page and tab services](../integration-reference.md#page-helpers-and-stable-tab-services), [native evidence](../../scripts/compatibility/evidence/page-helpers.json) and the [pinned reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-page-helpers.json).

## Historical webRequest

webRequest rules use official selectors, cancel/redirect actions and capture substitution. Registration replaces an execution's rules; an empty array revokes them. Static metadata rules are prepared before navigation. Native request ownership scopes rules to their browser/frame/document; unowned requests do not inherit another frame's rules. Listeners report asynchronously and cannot decide network I/O. Static loading cannot open an interactive permission prompt.

The [native matrix](../../scripts/compatibility/evidence/webrequest.json) verifies first-request effects, capture redirects, replacement and revocation. A pinned historical MV2 reference is unavailable: immediate void returns and first-match overlap behavior remain implementation decisions awaiting that comparison. WebSocket interception, OOPIF and isolated-frame dynamic page interception are not passed capabilities. See the [request interception guide](../usage-guide.md#request-interception) and [host integration](../integration-reference.md#web-requests).

## Tab audio

The native service provides mute state, mute changes and exact-function listeners on 84/121/151. External host changes are polled every 200 ms, so shorter transitions may be missed. Audible state is unavailable. In the tested 151 navigation, native mute resets to false; 84/121 retain it. The service reports native readback and does not promise retention on 151.

The pinned 5.5.1 reference resolves modern setMute with `{isMuted}`, differing from the current official Promise<void> description. See the [audio guide](../usage-guide.md#tab-audio), [native matrix](../../scripts/compatibility/evidence/audio.json) and [pinned reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-audio.json).

## Private contexts

The Demo uses real in-memory RequestContexts, temporary permissions/tab state and a private GM value store. A script's first private access copies its ordinary atomic baseline; private writes never return to ordinary storage. Private browsers drain before the last tab releases the shared private context. Diagnostics and automatic favicon loading avoid ordinary persistent paths; explicitly saved files and copied clipboard contents remain user outputs.

The [native/WPF matrix](../../scripts/compatibility/evidence/private-contexts.json) and [final delivery snapshot](../../scripts/compatibility/evidence/delivery.json) distinguish incremental checks from the final 84/121/151 x64 WPF coverage. See the [private browsing guide](../usage-guide.md#private-browsing) and [pinned temporary-session reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-private-tabs.json). Page-world trust still applies; Firefox containers are unavailable.

## Known evidence gaps

Guaranteed full-GM document-start and transparent BFCache restoration remain unavailable. Isolated OOPIF source/storage/resources/XHR now use native child sessions; native isolated child manual menus are verified on 84, while 121/151 remain main-only. See the precise [execution limits](execution-worlds.md#remaining-limits). Cookie partition CRUD on 121/151 does not imply XHR request-partition selection; see [partition scope](cookies.md#partitioned-cookie-scope).

The [seven-category corpus](../../scripts/compatibility/corpus/README.md) has pinned sources and functional runs. Full stable-reference and comprehensive acceptance remain incomplete: saved-file/ZIP/cancellation checks are blocked by reference browser download permission, the reference history-refresh assertion failed, and native notification/download interactions still lack comparisons. The [reference corpus](../../scripts/compatibility/evidence/corpus-reference-stable.json) and [delivery evidence](../../scripts/compatibility/evidence/enhanced-follow-up-delivery.json) preserve those outcomes. Native test totals are not a compatibility percentage.

The selected enhancement delivery passed its recorded native/package scope. Incremental non-fetch text, staged stream upload and isolated OOPIF execution have documented capabilities; full menu acceptance still lacks 121/151 isolated child routing and fixed-beta native click/overlap comparisons. BFCache and strict start/CSP work establish assessment results only. Request partitions, audible and historical MV2 parity remain unsupported or unverified; they were not implemented by the scoped delivery. See [network scope](network.md), [menu boundaries](host-interactions.md#menus), [execution limits](execution-worlds.md#remaining-limits) and [recorded validation](validation.md#latest-recorded-validation).
