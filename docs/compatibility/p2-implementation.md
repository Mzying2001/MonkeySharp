# P2 implementation record

The 2026-11-07 work follows steps 16–20 of the local compatibility plan, on
`refactor/tampermonkey-compatibility`. Each step is validated and committed before
the next starts. The outstanding step-15 historical MV2 reference and third-party
corpus are separate evidence gaps; they are not counted as passing here.

| Step | Status | Evidence |
| --- | --- | --- |
| 16. Historical webRequest | Implemented within the documented scope | [Six-engine matrix](evidence/webrequest-p2.json) |
| 17. Tab audio | Mute capability validated; audible unavailable | [Six-engine matrix](evidence/audio-p2.json), [pinned TM reference](evidence/tampermonkey-5.5.1-audio.json) |
| 18. Private tabs | Validated on the supported Demo gates | [Real contexts](evidence/private-contexts-p2.json), [pinned temporary-session reference](evidence/tampermonkey-5.5.1-private-tabs.json) |
| 19. Partitioned Cookie/XHR | CRUD validated; request selector unavailable | [Native matrix](evidence/partition-cookies-p2.json), [reference with deletion difference](evidence/tampermonkey-5.5.1-partition-cookies.json) |
| 20. Regression and delivery | Pending | — |

## 16. Historical webRequest and static rules

The script API now consumes an array of official selector/action objects. Cancel
and nested static/dynamic redirect actions replace the old DSL. Redirect regexes
use the same bounded ECMAScript subset as metadata, with case-sensitive capture
extraction and JavaScript replacement tokens. URL permissions and host policy
are checked for expanded targets. There are no script removal handles, custom
priorities, header mutations, or authentication actions.

Re-registration replaces an execution's rules/listener; an empty array revokes
them. A dynamic registration overrides that installation's metadata rules in its
own document. Scope uses trusted browser/frame/document identities; Core-created
HTTP requests carry their original frame internally. Requests without ownership
are not assigned to another frame. Navigation, repository changes, and detach
revoke registrations. Script listeners run asynchronously and retain the official
action/status/details payload. Their return values never control network I/O.

Metadata rules are compiled in the prepared revision. Attach initializes a
browser-scoped set and `PrepareAsync`/`NavigateAsync` await readiness. Updates,
disable, and uninstall revoke the old registration. Static rules honor URL,
noframes, privacy context, and host authorization. Loading a document cannot
create an interactive permission prompt. The adapter can defer a resource load
for an asynchronous policy; a synchronous redirect callback fails closed when
authorization cannot finish synchronously.

Validation passed: 381 .NET tests at both 84/121 x64 and 113 JS tests; all six SmokeHost/CDP combinations and both WPF gates. It includes original real-Chromium first
external-script cancellation and capture redirect, replacement/revocation,
server-side request counts, document cleanup, and the real WPF services. Raw
logs are under `artifacts/p2-step16-*`; the committed matrix records the exact checks. WebSocket handshakes reached the server in all six combinations, so cancellation of WebSockets is an expected engine limitation of this callback path, not passed support.

The [official contract](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_webRequest)
and [static metadata contract](https://www.tampermonkey.net/documentation.php?locale=en&q=webRequest)
are the target sources. A runnable pinned MV2 differential environment remains
unavailable. First-match overlap behavior and immediate void returns are therefore
implementation decisions awaiting that reference, not experimentally established
Tampermonkey parity. OOPIF and isolated-frame dynamic page interception remain
unverified/unavailable. The fixture probes WebSocket separately; listing a resource
type in Core is not evidence that CEF dispatches that type through resource
callbacks.

## 17. Tab audio

An independent ITabAudioService backs GM.audio/GM_audio. Exact function identity
controls listener removal; registrations are execution-owned. The default CEF
service reads IsAudioMuted and calls SetAudioMuted on the native UI thread,
verifies results, and observes persistent external host changes on a 200 ms poll.
Unknown causes are omitted from state and represented as unknown in changes.
Rapid transitions between polls cannot be reconstructed.

The pinned 5.5.1/MV3 experiment confirms modern Promise and legacy callback
returns, extension/false listener events, and setMute resolving {isMuted:true}.
That last result differs from the current official Promise<void> description;
the implemented facade follows the pinned observable result. The legacy root
is a function namespace, also observed in that reference.

Audible is deliberately unavailable. The CEF mute interface provides no reliable
aggregate audible observation; PCM/stream lifecycle has not established that
contract. Tests exercise WebAudio and a zero-gain stream while verifying that
neither fabricates audible fields. The info status explicitly says mute-only.
Real browser tests also cover callback completion, duplicate/removing listeners,
multiple frames, external native mute and navigation matching the actual native state.

Validation passed: 384 .NET tests at both 84/121 x64, 114 JS tests, all six
SmokeHost/CDP combinations (14 cases), and both WPF gates including native audio.
The navigation probe records native true before navigation and false afterwards
on 151; 84/121 retain true. This is an observed native-engine boundary, not passed
retention support on 151. The facade reports the readback faithfully.

## 18. Private contexts

The Demo binds each tab to an actual TabContext. A private group uses a new
in-memory RequestContext, independent permissions and tab state, and a private
value store. All private browsers drain and detach before the last one releases
that context. Explicit incognito options are preserved by both facades and Core;
hosts that lack genuine private creation reject before opening.

The pinned 5.5.1/MV3 split-mode experiment confirms that private code reads the
normal baseline but its writes stay private. Normal legacy and modern reads
remain unchanged, including after restarting the manager. This matches the
[maintainer's explanation](https://github.com/Tampermonkey/tampermonkey/issues/2432)
that private data must not return to regular sessions. The implementation lazily
copies each script's atomic normal snapshot at first private access, then uses
only the private in-memory journal and listeners until session disposal.

Native WPF tests cover info/run-in consistency, normal/private Cookie and HTTP
separation, cache isolation/sharing/last-close clearing, memory-only remembered
permissions, stable private tab data, copied GM values with no normal writes,
coexisting tabs, explicit tab handles and fresh private sessions. Private logs
and favicons avoid persistent profile paths; native file logging is disabled.
Explicitly saved downloads remain, while their private transient history is
removed when the owning tab closes.

The stress fixture also exposed a stale-frame boundary in the step-16 request
adapter. It now captures immutable ownership when a resource request starts and
does not read a destroyed frame from completion callbacks. An invalid native
frame produces unknown ownership instead of escaping through a CEF callback.
The adapter regression test covers that late callback path.

Validation passed: 389 .NET tests at both 84/121 x64, 114 JS tests, all six SmokeHost/CDP combinations, and both WPF private-context gates. On 151, ownership is captured at the first OnBeforeResourceLoad because frame URLs may be unknown at handler construction. Static first-script interception and late-callback cleanup both pass. The Demo uses tabs within its WPF window; private WPF proof covers 84/121 x64 and retains the TrustedPageWorld boundary. Firefox containers are not supplied.

## 19. Partitioned Cookie scope

The existing CDP service now supports typed partition selectors on the tested
121/151 engines. It uses native string/object protocol forms without branching
in Core or JS. Fixed Chromium public-suffix snapshots normalize sites and derive
151 set defaults accurately, including ICANN/private rules and exceptions.
84 rejects explicit partition parameters before Cookie effects; 121 rejects the
unavailable ancestor bit. Empty list filters cover serializable normal/partition
identities; no opaque/nonce key support is claimed.

Deletion expires exactly one complete identity, because older deleteCookies
commands cannot select a partition. The real fixtures preserve ordinary and
partition siblings during both ordinary and partitioned deletion. Set derives
an omitted 151 ancestor bit, list treats it as a filter, and delete defaults to
the cross-site ancestor identity as observed in the reference.

The pinned 5.5.1 reference confirms CRUD selection, separate ancestor identities,
request cookiePartition selection and incoming partition placement. Its checked
preservation assertion fails: deleting partition A also removes the ordinary
same-name cookie. The raw record remains failed for that assertion, not passed
evidence. MonkeySharp intentionally preserves the other identity, consistent
with its precise-delete requirement. The source fixture and result are kept
without rewriting the observation.

Native CEF probes inspect the public request surface and send/receive real TLS
traffic. No top-level partition selector exists; the request sends ordinary
state and stores a Partitioned response under its own site rather than top-a.
Consequently request cookiePartition stays explicitly unsupported before send.
No Cookie-header synthesis, jar swapping or second transport is introduced.
CRUD support is recorded separately from this request limitation.

Public-suffix data and its MPL-2.0 notice/provenance are embedded and included in
packages; no new native API or higher minimum framework is introduced on the
84 basic path. See the [third-party notice](../../Mzying2001.MonkeySharp.CefSharp/Protocol/THIRD-PARTY-NOTICES.md).

Validation passed: 403 .NET tests at both 84/121 x64, 114 JS tests, all six SmokeHost/CDP combinations with 15 cases, and WPF84 rejection/WPF121 CRUD. The native request limitation and pinned-manager preservation failure are separate from passed CRUD checks. The new suffix data is pinned to Chromium121.0.6167.160 and151.0.7922.174.

## 20. Final isolated delivery

The six detached checkouts passed complete builds/tests, real Chromium/CDP, unique-version NuGet packaging and public consumers. All three x64 WPF gates passed, including private contexts; CI now keeps that 151 gate. The original local search script ran unchanged through each package consumer, with DOM, storage, empty submission and navigation effects verified. Its source remains local and unredistributed; the pending third-party corpus and historical/native limits remain explicit. See the final delivery record for package, path, SDK and provenance corrections.
