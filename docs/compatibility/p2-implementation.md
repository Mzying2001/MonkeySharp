# P2 implementation record

The 2026-11-07 work follows steps 16–20 of the local compatibility plan, on
`refactor/tampermonkey-compatibility`. Each step is validated and committed before
the next starts. The outstanding step-15 historical MV2 reference and third-party
corpus are separate evidence gaps; they are not counted as passing here.

| Step | Status | Evidence |
| --- | --- | --- |
| 16. Historical webRequest | Implemented within the documented scope | [Six-engine matrix](evidence/webrequest-p2.json) |
| 17. Tab audio | Pending | — |
| 18. Private tabs | Pending | — |
| 19. Partitioned Cookie/XHR | Pending | — |
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
