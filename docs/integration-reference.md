# MonkeySharp Integration Reference

**English** | [中文](integration-reference.zh-CN.md)

This reference collects the compatibility, network API, host service, and verification details that are useful while implementing or maintaining a MonkeySharp host. Start with the repository [README](../README.md) for installation and minimal integration. The [userscript guide](usage-guide.md) covers script-facing parameters, return values, and examples; this reference focuses on host implementation and verification.

## CefSharp Version Compatibility

The adapter cannot support arbitrary CefSharp versions from one binary. CefSharp assemblies are strong-named, and public interfaces have changed between releases. For example, `IBrowser.GetFrame(long)` became `GetFrameByIdentifier(string)`, while `IFrame.Identifier` changed from `long` to `string`.

The adapter defaults to `151.3.240` and has source compatibility shims verified against `84.4.10`, `121.3.70`, and `151.3.240`. Build and test the adapter against every version shipped by the application:

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

The adapter's `CefSharp.Common` reference is private to its NuGet package. Consumers must reference the matching `CefSharp.WinForms`, `CefSharp.Wpf`, or `CefSharp.Common` package and allow the normal .NET Framework binding redirects. Keep the managed packages, native CEF binaries, and MonkeySharp adapter on the same version and process platform.

Keep all projects on the selected `CefSharpVersion`; see [build and validation](#build-package-and-verify) for commands and the CI matrix.

## API Contract and Grants

Modern `GM.*` methods and legacy `GM_*` globals share one contract. Legacy globals require exact declared names; modern storage, resource, menu and tab-data methods are authorized as families. For example, `GM_getValue` also enables modern `GM.setValue`, but not the undeclared `GM_setValue` global. See the [script-facing grant rules](usage-guide.md#execution-grants-and-compatibility).

Page-world executions with recognized grants other than info require `TrustedPageWorld`, which defaults to false. Explicit DOM/content execution uses verified isolated transport instead. Grants, host services and permission policy still apply in either world. The [README security table](../README.md#security-model) explains when to opt in; [native isolated execution](#native-isolated-execution) describes adapter boundaries.

`GM.info` and `GM_info` need no grant or bridge. `IDownloadService.DownloadMode`, `CefSharpHostOptions.IsIncognito` and `AutomaticallyUpdatesScripts` supply actual service/context/update facts; unknown states are omitted. The Demo reports each tab's normal/private context and manual-only updates. The manager version is `GM.info.version`; the script version is `GM.info.script.version`.

`UserScriptExecutionEnvironment` preserves custom `capabilityStatus` keys, including subfeatures. Report only facts the host can establish; API availability does not imply every subfeature is supported. See the [information reference](usage-guide.md#information-and-logging) for fields and adapter capability keys.

## Host Service Contracts

The builder is single-use. The application owns the repository, browser and objects supplied through `UseValueStore`, `UsePermissionPolicy` or `Use...Service`. The host owns its engine, gateway, internally created providers, default services and default value store. Objects passed to `AddApiProvider` transfer to the gateway and are disposed when applicable. Factory-created download services are host-owned.

The following example assumes the repository and service instances have already been created. Register only the services the application supplies. `TrustedPageWorld` is appropriate only for trusted pages and scripts:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .UseWindowService(windowService)
    .UseHttpRequestService(httpRequestService) // optional default override
    .UseCookieService(cookieService)           // optional default override
    .UseWebRequestService(webRequestService)   // optional default override
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

For durable installations, replace the in-memory repository in the example above with a `PersistentUserScriptRepository` backed by an application-owned `IUserScriptRepositoryPersistence`. Initialize it before using it or attaching a host:

```csharp
using System.Threading;

var repository = new PersistentUserScriptRepository(persistence, parser);
await repository.InitializeAsync(CancellationToken.None);
```

The persistence implementation must preserve `ScriptKey`, installation order, and timestamps, and commit mutations atomically. Cancellation after a successful commit must complete successfully; repository change notifications follow commit. Stable keys retain the association with GM storage. See the [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md#portable-data-and-recovery) for a concrete persistence and recovery implementation.

Provider requirements:

- `IUserScriptValueStore.SetAsync` accepts an immutable `UserScriptValue`; `StoredValue.Value` exposes it and `StoredValue.Exists` alone distinguishes absence. Use `UserScriptValue.FromJson(json)`, `UserScriptValue.Undefined`, or `UserScriptValue.FromEncoded(encoded)`.
- Persist `EncodedValue` without dropping type information. Encodings are `u` for undefined, `btrue`/`bfalse`, `n` followed by a number or `NaN`/`Infinity`/`-Infinity`, `s` followed by literal string contents, and `o` followed by canonical object/array/null JSON. `FromEncoded` validates and canonicalizes them; a user object's property names never act as protocol markers.
- Operations for one `ScriptKey` must be linearizable. Keys may be empty strings, but script partition IDs must be non-empty. Cancellation before commit makes no change; cancellation after commit succeeds. Emit one `ValueChanged` event per actual committed change, including creation/deletion of stored undefined; identical encoded writes and deletion of absent keys emit none. `ListKeysAsync` preserves insertion order, with updates retaining position and delete/reinsert moving a key to the end.
- `IUserScriptValueStore` requires atomic `GetSnapshotAsync` (values plus partition watermark), `GetChangesAsync` (contiguous suffix after a watermark), and `ApplyAsync` (validated atomic batch with request/execution provenance). Subscribe to `ValueChanged` before taking a snapshot, then replay changes after its watermark. No per-key snapshot fallback exists. In-memory and SQLite stores retain 4096 changes per script; an expired/future watermark requests a fresh snapshot. Watermarks belong to one live store session; execution credentials expire when the host restarts.
- Bulk writes/deletes validate and commit through one `ApplyAsync`; failures publish no partial keys or events. Committed changes retain `mutationId`, `originExecutionId`, method and per-script sequence. This atomicity is a MonkeySharp store contract, not a guarantee attributed to Tampermonkey. Bulk reads consume one atomic snapshot. `ApplyAsync` returns a `UserScriptValueCommit` with the exact commit watermark even for no-op batches. The JS facade uses that acknowledgement to confirm replay before fulfilling a write Promise. Its confirmed mirror and ordered pending operations are distinct; local callbacks run synchronously and committed echoes are suppressed.
- `IUserScriptPermissionPolicy` receives the installation, frame, method, target summary, and current capabilities for every host-backed request.
- `IUserScriptWindowService` backs the exact `window.close` and `window.focus` grants. `CloseAsync` may refuse to close the last tab; omit `UseWindowService` to leave both properties unavailable. `window.onurlchange` is core-only and reports same-document `pushState`, `replaceState`, `popstate`, and `hashchange` changes.

`IUserScriptDependencyProvider` and `IResourceProvider` supply installation content to the repository's `UserScriptPreparer`, not to each execution. Missing providers or fetch/validation failures fail installation/update before publication; resource APIs consume the prepared revision. See [preparation and integrity](#resource-integrity-and-update-checks).

`IMenuService.RegisterAsync` receives `Action<MenuCommandInvocation>`. `MenuCommandRequest.OriginFrame` identifies the authenticated subscriber; `ClickedFrame` requests invocation only for a matching host-attested current frame. Populate only input fields actually observed. Use `host.ResolveMenuFrame(nativeFrame, request)` for native frame resolution; null means unknown/stale and suppresses a targeted invocation. The Demo popup has no frame identity. This GM callback contract is separate from `@run-at context-menu` source execution. See [menu semantics and evidence](compatibility/host-interactions.md#menus).

Interaction services have distinct lifecycle contracts:

| Service entry point | Returned handle | Lifecycle members |
| --- | --- | --- |
| `INotificationService.Create` | `INotificationHandle` immediately | Unstarted handle: subscribe before `Start`; `Clicked` awaits `ResolveClick(preventDefault)`, then `Closed`/`Completion`; `Dispose` closes it. |
| `ITabService.OpenAsync` | `Task<ITabHandle>` | `TabId`, `Closed`, `OnClose`, `CloseAsync`, and `Dispose`; there is no `Completion` task. |
| `IDownloadService.CreateAsync` | `Task<IDownloadOperation>` | `Start`, `Result`, `DownloadId`, `Completion`, `Progress`, `Completed`, `Failed`, `Aborted`, `TimedOut`, `Abort`, and `Dispose`. Create must return an unstarted operation; subscribe to every event before Start. |

Use `UseDownloadServiceFactory(http => new FileDownloadService(http, selectDestination))` to share the effective CEF network service (including webRequest composition) with XHR. The builder owns factory-created disposable services. `selectDestination` is trusted host policy/UI returning an absolute `DownloadDestination` or null to cancel. Browser-mode hosts implement picker and conflict options; native-mode hosts reject them before effects. Dispose cancels accepted tasks; await `DrainAsync` before shutting down CEF. `DownloadRequest.Anonymous` and `RedirectAllowed` must be honored. Each operation emits exactly one terminal event; successful `Result` describes the committed file. Never start transfer or UI from CreateAsync.

The runtime uses handle events to deliver script callbacks; completing a `Completion` task alone does not send a terminal callback. Providers should emit one terminal event and support idempotent cleanup when the execution ends. Releasing a tab handle should release its observations; closing the user tab is a separate `CloseAsync` action.

### Page helpers and stable tab services

`GM.addStyle`/`GM.addElement` run locally and obey page CSP; their script signatures are in the [DOM guide](usage-guide.md#dom-helpers). `GM.log` preserves original console arguments locally and sends only a bounded best-effort JSON snapshot to `LogTo`.

`OpenTabRequest` supports adjacent `Insert` and absolute `InsertIndex` (the index wins). `ITabHandle` must report actual `Closed` state and one closure event; renderer correlation covers closure before acknowledgment. Releasing a handle removes observations; `CloseAsync` is the separate action that closes the user tab.

`ITabStateService` binds values to a stable host tab identity across documents and scopes them by `ScriptKey`. The Demo binds SQLite state to `BrowserTabViewModel.TabId`, deletes it on close and clears stale state on startup. Its window policy refuses to close the last tab. Tab save/read operations serialize per execution and capture the saved JSON object before queueing; see [tab state](usage-guide.md#tabs-and-tab-state) for script conventions.

## Resource Integrity and Update Checks

Configure installation content with `new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(dependencyProvider, resourceProvider))`, or pass the same preparer to `PersistentUserScriptRepository`. Install, edit and update await complete preparation before publishing a revision; a failed update leaves the active revision intact. All frames consume the captured revision without further content-provider calls. Dependencies share one scope and retain directive/strict semantics, with newline/semicolon boundaries between source segments.

`ResourceContent` retains original bytes, MIME, optional charset and optional host-decoded text. The host text takes precedence; otherwise charset selects the decoder, with UTF-8 as the default. Binary data URLs use the original bytes. The Demo atomically stores prepared content with the script, validates its content digests on reload, and upgrades format-2 databases with an additive table. Existing records are prepared once during initialization; unavailable content fails initialization explicitly and preserves the database. Installation providers own trusted network policy separately from GM network authorization.

The preparer defaults to a 32 MiB combined source/content budget. Resource packages have a separate 64 MiB serialized bootstrap budget and use indexed response chunks when needed. User code starts only after complete reassembly; missing, duplicate or truncated chunks fail execution. See [resource and storage evidence](compatibility/README.md#storage-and-resources).

`@require` and `@resource` may carry `md5`, `sha1`, `sha256`, `sha384` or `sha512` hashes in hexadecimal or standard Base64. Both `#algorithm=value` and `#algorithm-value` forms are accepted, with comma/semicolon-separated hashes; the last supported declaration is selected. The fragment is stripped from the requested and cached URL. When integrity is declared, the installation preparer, resource API, and Demo HTTP cache verify original bytes before use, and cached bytes are verified again when read. An unsupported algorithm produces a warning; malformed declarations, no usable supported hash among the declarations, and digest mismatches fail closed and prevent the resource from loading. Resources without integrity declarations load without hash verification.

The Demo manager offers a manual check-and-install action. `@updateURL` is preferred as the check source and `@downloadURL` as the download source; `@downloadURL none` disables checks. A candidate must have the same non-empty `@name` and `@namespace`, and the downloaded version must be newer than the installed one and no older than the checked version. Applying it preserves the installation key, enabled state, installation time, and GM storage while replacing source and update metadata. Checks use the Demo's separate HTTP client without browser login cookies; there is no background schedule or runtime bridge update API.

## Storage snapshots and listeners

The host atomically captures each script's values and watermark after subscribing to changes. Complete indexed snapshot chunks are reassembled and subsequent changes replayed before user code starts. Store sessions use per-script sequences and a 4096-change journal; expired history requires a fresh snapshot rather than invented listener events.

The renderer maintains confirmed state plus ordered pending mutations. Local reads see that combined mirror; local listeners run before setters return, while modern writes wait for host commit/replay acknowledgment. Failed writes are removed and later pending writes replayed, so an old failure cannot overwrite newer local/remote state. Committed echoes are suppressed and remote changes notify once. Replay loss recovers current values and reports `MSC414_STORAGE_REPLAY_RESET`; unavailable historical callbacks cannot be recovered. Registration/removal takes local effect immediately, and execution/document cleanup revokes runtime-owned listeners.

Custom stores must satisfy the [atomic snapshot/batch contract](#host-service-contracts). These batch guarantees belong to MonkeySharp; Tampermonkey transaction or identical IPC-latency guarantees are not claimed. See [storage evidence](compatibility/README.md#storage-and-resources).

## Prepared execution conditions

`PreparedScriptRevision.UrlRules` compiles URL rules and repeatable run-in selectors before activation. Matching reuses immutable rules across frames, with metadata-lifetime caching. Include globs use bounded scheme/host wildcards, literal question marks and case-insensitive comparison; `/expression/` uses the documented ECMAScript subset with a 100 ms timeout. Timeouts fail closed with `MSR214_URL_RULE_TIMEOUT`. IPv6, IDN, explicit/wildcard ports, queries, fragments and escaped unreserved characters are covered by the fixed manager fixture.

`RunIn` is now a read-only selector list. `CefSharpHostOptions.IsIncognito` must report the real host-owned request context; an unknown context rejects restricted execution with `MSR211_RUN_IN_UNAVAILABLE`. Container declarations warn with `MSM083_UNSUPPORTED_CONTAINER`; unknown selector syntax is an installation error. Manual context-menu execution ignores include/exclude, while retaining match/exclude-match, noframes, privacy and trust checks. These checks do not create private contexts.

Unicode host text in `@match` is retained but does not match normalized browser hosts in the pinned manager. Use Punycode; installation reports `MSM084_UNSUPPORTED_IDN_MATCH` for this ineffective declaration. Punycode rules match canonical IDN browser URLs.

## Interactive authorization

For consent-based hosts, share `InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), decisionStore, prompt)` across tabs. Declarations are checked before consent: XHR uses connect, Cookie uses match/include, and download uses its host policy. Initial requests and redirects pass the same decision path. Allow/Deny from an explicit host policy are authoritative; Prompt consults exact remembered scopes and then the asynchronous interaction. A bare unresolved Prompt is a host configuration error, not an implicit Deny. A host without UI explicitly supplies `RejectUserScriptPermissionPrompt`.

Scopes use stable installation key + permission kind + canonical HTTP(S) origin, with no credentials, path or query. Allow-once applies only to the currently coalesced batch; completed prompts cannot authorize later requests. Remembered decisions can be revoked. One canceled waiter does not cancel another document's joined request; all canceled waiters close the prompt. Navigation cancels interactive waits, and installation revision/enabled state is checked again after consent.

Generic execution timeout starts after authorization. CEF transfer and download deadlines pause while redirect permission waits and then resume their remaining active budget. Providers validate declarations before effects as well. Demo uses an additive SQLite decision table, real asynchronous permission windows and a permission-management button; revocation cancels pending prompts and reloads affected execution state. Its scripted E2E clicks the actual permission window and checks persistence.

## Network APIs

The adapter supplies default HTTP, Cookie and webRequest services using the attached browser's actual RequestContext. Set `EnableDefaultNetworkServices = false` to omit all three defaults, or replace them individually with `UseHttpRequestService`, `UseCookieService` and `UseWebRequestService`.

### HTTP Requests

The [HTTP request guide](usage-guide.md#http-requests) owns request parameters, return values, callbacks and script examples. The default service uses CEF URLRequest rather than page fetch. Ordinary requests use the browser's applicable cookies/credentials; anonymous requests suppress them. Explicit `cookie`, `headers.Cookie`, request `cookiePartition`, proxy and synchronous requests are rejected before network effects. See [network scope](compatibility/network.md) and [explicit Cookie limits](compatibility/cookies.md#explicit-request-cookies).

Custom `IHttpRequestService` implementations receive a repeatable `IUserScriptHttpBody` and an observer. Report metadata before body data, honor `UserScriptHttpRequest.MaxResponseBytes` (`null` means unlimited), call `RedirectAllowed` before following each redirect, and record followed URLs for Core to recheck against `@connect`. This body limit differs from the single-message `BridgeOptions.MaxResponseBytes`. Cross-origin redirects remove Authorization. Custom explicit-Cookie support must independently verify merge semantics.

### Binary transport and stream completion

Request/response bodies and staged downloads have a default 256 MiB total budget (`BridgeOptions.MaxBinaryTransferBytes`), independent of single-message limits. Transfers use at most 64 KiB per bridge chunk; JSON/Base64 overhead must still fit the configured message budget. Host spools retain up to 256 KiB in memory, then use temporary files. Non-stream responses are assembled in renderer memory.

ReadableStream request bodies are authorized before acquiring a reader, staged through acknowledged `appendBody` calls, then sent by `execute`. `IUserScriptHttpBody` can reopen the completed spool for redirects without rereading the source stream. CEF accepts one PostData element: bytes for small uploads or a temporary file for larger bodies, released at termination. This is staged upload, without live renderer-source network backpressure.

Non-fetch text/JSON/document/XML progress exposes cumulative decoded `responseText`; text also exposes cumulative `response`. A persistent decoder preserves split multibyte characters, while JSON/document parsing remains final-only. Binary/stream responses have no partial text. Fetch semantics suppress progress and intermediate readyState events; supplying `redirect` (including `"follow"`) or setting `anonymous: true` also selects those semantics. See the [response contract](usage-guide.md#response-and-callbacks) and [network evidence](compatibility/network.md).

Stream response Promises resolve when headers and the stream are available; later failures error the stream, while `onload`/`onloadend` follow network completion. Renderer pulls and host spooling bound bridge queues and disk usage, but cannot pause CEF's network source. Stream spools are released at EOF, cancellation or execution end.

HTTP `execute` and `readBody` are exempt from `BridgeOptions.RequestTimeout`; other session operations use the ordinary timeout. All requests, including `execute`, count toward `MaxPendingRequestsPerDocument`. The HTTP request's own timeout is independent, except under fetch semantics.

Blob/File downloads stage acknowledged chunks before entering the save/abort/atomic-commit pipeline. Custom download services must read `DownloadRequest.Body`, rather than dereferencing its blob identifier through HTTP, and keep the body available until the terminal event. Failed or incomplete staging is released on failure/navigation. Accepted downloads can outlive their document with callbacks detached; host disposal cancels them.

### Cookies

The default `CefSharpCookieService` uses the host's shared CDP session and attached RequestContext; construct it with `new CefSharpCookieService(host)`. Core checks grants, script URL scope and host authorization. The backend maps SameSite, reads back actual identity/attributes, and verifies exact deletion. `ICookieService` implements CRUD only, without custom listeners. Script parameters and return conventions belong to the [Cookie guide](usage-guide.md#cookies).

### Partitioned Cookie scope

The default backend supports serializable `partitionKey` CRUD on tested 121/151 engines; 84 rejects explicit partition keys before effects. The ancestor dimension is available on 151 and rejected on 121. Normalization uses the public-suffix snapshot matching the Chromium release. Opaque/nonce keys are unsupported. See the [partition input and identity rules](usage-guide.md#partitioned-cookie-scope).

Custom partition backends implement `IPartitionedCookieService.NormalizePartitionKeyAsync` in addition to CRUD, validating actual support and canonicalizing keys before Core filters results. Other backends reject `partitionKey` instead of silently accessing ordinary storage.

Partition CRUD does not enable XHR `cookiePartition`: CEF's public request API cannot select the required trusted top-level partition. The default backend rejects it before dispatch. See [Cookie scope and evidence](compatibility/cookies.md#partitioned-cookie-scope).

### Web Requests

Whenever a webRequest service is available, including the default, `Attach` requires an empty `RequestHandler` slot or a `CefSharpWebRequestHandlerMultiplexer`. Attach before initialization to cover initial resources. Late attachment is allowed and marked by `IsLateAttach`, but cannot intercept requests already sent. Compose an existing application handler before attaching:

```csharp
var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
requestHandlers.Add(applicationRequestHandler);
browser.RequestHandler = requestHandlers;
host.Attach(browser);
```

The adapter rejects occupied non-multiplexer slots. Explicitly supplying `UseWebRequestService` still requires this composition when defaults are disabled. Render handlers separately require a `RenderProcessMessageHandlerMultiplexer`, as shown in the [README](../README.md#existing-browser-handlers).

Static `@webRequest` rules compile at installation and activate before navigation through `host.NavigateAsync`; repository changes revoke old rules. Static loading follows URL/frame/run-in/host policy and cannot open interactive permission prompts. Dynamic rules belong to one browser/frame/document/execution and are revoked on navigation, update, disable, uninstall or detach. Unowned requests do not inherit another frame's rules.

Selectors, actions, replacement/revocation and callbacks are documented in the [request interception guide](usage-guide.md#request-interception). Historical MV2 parity, OOPIF, isolated-frame dynamic page interception and WebSocket coverage have explicit [evidence limits](compatibility/README.md#historical-webrequest).

## Private browsing

A host advertising `ITabService.SupportsIncognito` must open an actual private RequestContext; unsupported services reject private requests before opening. Resolve `CefSharpHostOptions.IsIncognito` from the context the host owns, so `@run-in`, `GM.info.isIncognito`, network services and tab opening agree. Omitted `incognito` inherits the source tab in the Demo; false explicitly selects a normal tab.

The Demo's `TabContext` binds stable tab identity, RequestContext, privacy state, value store, permission policy and tab-state service. Private tabs share a separate in-memory context, temporary permissions/tab data and per-script copies of normal GM baselines; private writes/listeners never flow back to normal storage. After the last private tab closes, drain its host tasks before releasing the context and clearing the session.

For profile handling, saved-file/clipboard exceptions and script behavior, see [private browsing](usage-guide.md#private-browsing) and the [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md). [Compatibility evidence](compatibility/README.md#private-contexts) records the validated context isolation.

## Tab audio

The default `ITabAudioService` provides native mute read/write and change listeners on 84/121/151. It runs native access on the CEF UI thread and polls external host changes every 200 ms, so shorter transitions may be missed. It does not provide a reliable audible source; capabilities report `partial:mute-only;audible-unavailable`. The tested 151 navigation resets native mute, so retention across navigation is not guaranteed.

Override it with `UseTabAudioService`, or set `EnableDefaultAudioService = false` to omit the default; network-service options do not control audio. Application-supplied services remain application-owned. See the [audio API](usage-guide.md#tab-audio) and [native evidence](compatibility/README.md#tab-audio) for state, callbacks and version differences.

## Lifecycle and Diagnostics

`PrepareAsync()` prepares the attachment's shared CDP session; `NavigateAsync(url)` waits for it before navigating. Start the browser at `about:blank` when using this sequence. `IsLateAttach` and `IsLatePreparation` identify initialization that already occurred. These APIs do not upgrade userscript timing guarantees. See the [real protocol findings](compatibility/execution-worlds.md#protocol-session-and-preparation).

The adapter creates an identity per frame document and executes each `(document, frame, script, run-at)` once. `@run-at context-menu` scripts are listed by the native CefSharp context menu for the matching frame and execute a fresh invocation only after the user selects an entry. Navigation, context release and detach invalidate document capabilities and pending calls; both `ContextReleased` and `NavigationSuperseded` retire a document's executions. Accepted downloads continue as host tasks with document callbacks muted; host disposal cancels them.

`GM.info` is a fixed per-execution snapshot; later executions use the current document URL and environment facts. Same-document URL changes do not reinject scripts. Use the granted [window.onurlchange API](usage-guide.md#window-extensions) for SPA updates.

CefSharp exposes only best-effort document-start hooks. Selected document-start executions emit `MSR100_DOCUMENT_START_BEST_EFFORT`. Set `RequireGuaranteedDocumentStart = true` to skip them with `MSR101_DOCUMENT_START_UNAVAILABLE`.

Before matching, the adapter reads the current renderer's `location.href`: older CefSharp context-created callbacks may retain the previous navigation URL. Canceled read-only evaluations are retried within a 15-second initialization budget, with at most two seconds per attempt and a 50 ms delay capped by the remaining budget. Cancellation, detach, replacement of the document session or native frame retirement stops initialization; a stale callback URL is never used as a fallback. An exhausted evaluation budget produces an initialization diagnostic containing a `TimeoutException` and the last cancellation. Failed lookup skips the document, including load fallback and context-menu execution. Document-start remains best effort.

Protocol errors use stable codes `MSP001` through `MSP010` and `MSP999`. Diagnostics never include capability tokens or stored values. Configure limits by assigning a new `BridgeOptions(...)` to `CefSharpHostOptions.Bridge`:

| Option | Default | Scope |
| --- | --- | --- |
| `MaxRequestBytes` | 1 MiB | Each UTF-8 bridge request, including HTTP body chunk messages. |
| `MaxResponseBytes` | 1 MiB | Each UTF-8 bridge response, including the combined hello response and body reads. |
| `MaxResourceBytes` | 10 MiB | Each resource exposed through the resource API. Installation content has a separate `UserScriptPreparer` budget. |
| `RequestTimeout` | 30 seconds | Ordinary bridge requests; HTTP `execute` and `readBody` are exempt. |
| `MaxPendingRequestsPerDocument` | 64 | Concurrent bridge requests for a document, including HTTP `execute`. |
| `MaxCompatibilityBootstrapBytes` | 64 MiB | Total serialized snapshot budget. Storage and resource snapshots larger than one response are transferred in indexed chunks before user code starts. |
| `CompatibilityBootstrapTimeout` | 5 seconds | Preparation of each storage or resource snapshot. |
| `MaxReplayEntriesPerExecution` | 4096 | Recent request IDs retained to reject replays for an execution. |
| `MaxBinaryTransferBytes` | 256 MiB | Each request/response body or staged download, independent of bridge message size. |

Snapshot preparation failure prevents user code from starting and reports `MSC410_COMPATIBILITY_BOOTSTRAP_FAILED`. Size errors require a budget adjustment. Storage replay loss reports `MSC414_STORAGE_REPLAY_RESET` and restores current values without inventing missed callbacks; see [storage synchronization](#storage-snapshots-and-listeners).

## Native isolated execution

The adapter uses native CDP frame/loader/context events, installation/revision scopes, context-ID bindings and remote global-object IDs for isolated source/delivery. Object IDs pin context generations and prevent numeric ID reuse from retargeting a response. The page binding accepts only active page-world document IDs; isolated proofs cannot pass through it. Shared protocol domains and other observers remain intact. `DocumentFrame` exposes actual `ExecutionWorld`, optional `ScriptScope` and `RevisionScope`; frame IDs are opaque and CDP IDs are never cast from CEF IDs. `UserScriptExecutionEnvironment` supplies service/privacy/update facts; execution-world info derives from the native frame.

`@sandbox DOM`/`@inject-into content` select isolated contexts; raw/default/JavaScript select page access. DOM plus an explicit `unsafeWindow` grant, conflicting worlds and unsupported unwrap reject with `MSR215_EXECUTION_WORLD_UNAVAILABLE`, without raw fallback. Isolated OOPIF uses native child sessions for the validated source/storage/resource/XHR scope, including normal/private contexts and migrations; see [OOPIF routing](compatibility/execution-worlds.md#isolated-oopif-routing).

BFCache cached executions are revoked and require fresh navigation. Historical page-frame webRequest is unavailable from isolated contexts. Page-world manual menus use the current native CEF frame session. Isolated manual menus support the main frame on all three tested versions and same-process child frames on 84; 121/151 remain main-only, and isolated OOPIF menus are unsupported. [Execution evidence](compatibility/execution-worlds.md) and the [start/CSP decision](compatibility/start-csp-decision.md) keep these limits separate from strict timing and CSP effects.

## Bridge Protocol

The bridge uses protocol `2`. Messages declaring a different protocol version are rejected with `MSP001_PROTOCOL_VERSION`.

## Build, Package, and Verify

For a clean source baseline, run `./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10` (including WPF on x64). For the full native/package gate, use `./scripts/compatibility/Run-Delivery.ps1`; it isolates each version/platform, verifies packages and runs public-API consumers. `-CorpusScript <path>` optionally adds a local script unchanged; `-PackageOnly` skips baseline checks and is diagnostic only. See [validation](compatibility/validation.md#reproduction) for scope and [tooling instructions](../scripts/compatibility/README.md) for parameters.

CI runs on PRs targeting `dev` or `main`, with CefSharp `84.4.10`, `121.3.70` and `151.3.240` on x64/x86; WPF gates run on x64 only. Its toolchain is Windows Server 2022, .NET SDK `9.0.315` and Node.js `24`, tested versions rather than minimum requirements. The SDK must support `MonkeySharp.slnx`; Core tests target `net8.0`, while CefSharp tests/apps target `net462`. Real-Chromium gates require a Windows desktop session.

Use the same `CefSharpVersion` property for restore, build, test, and package commands:

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`. The x86 solution configuration excludes the x64-only Demo projects. Package Core and each adapter platform into separate directories:

Both solution platforms map Core to AnyCPU, with outputs under `bin/Release/<framework>/`. Use `-p:Platform=AnyCPU` when packing Core, including with `--no-build` to reuse solution outputs. The CefSharp adapter requires its explicit x64 or x86 platform.

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=151.3.240 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=151.3.240 -o artifacts/packages/x86
```

Run the real-Chromium gates from a Windows desktop session:

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 151.3.240
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 151.3.240
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 151.3.240
```

Use the same version value throughout validation: `-p:CefSharpVersion=<version>` for `dotnet restore/build/test/pack`, and `-CefSharpVersion <version>` for the PowerShell runners. Run the SmokeHost commands for x64 and x86 at `84.4.10`, `121.3.70`, and `151.3.240`; the Demo gate covers x64 at all three versions. When switching versions in an existing checkout, first run `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<target-version>` (use the target platform) to avoid mixing incremental outputs from different CefSharp versions. The SmokeHost runner already restores, cleans and builds with the selected version, then verifies the running assembly version and process platform. Both gates use isolated profiles and `TrustedPageWorld`; they are integration checks, not security sandboxes.

### Chromium Compatibility Script Package

The SmokeHost installs repository-owned userscripts against loopback HTTP fixtures. Its runner requires all 15 compatibility cases, checks actual CefSharp version/platform and cleanup, and preserves logs under `artifacts/smoke/<version>-<platform>/`. Override that path with `-OutputDirectory`.

See [SmokeHost cases and result validation](compatibility/validation.md#smokehost-cases-and-results) for case IDs, assertions, timeouts, parser checks and log triage. The [validation guide](compatibility/validation.md#latest-recorded-validation) owns recorded results and remaining gaps; [tooling instructions](../scripts/compatibility/README.md) own runner parameters and pinned-reference prerequisites.
