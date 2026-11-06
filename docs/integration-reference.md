# MonkeySharp Integration Reference

**English** | [中文](integration-reference.zh-CN.md)

This reference collects the compatibility, network API, host service, migration, and verification details that are useful while implementing or maintaining a MonkeySharp host. Start with the repository [README](../README.md) for installation and minimal integration. The [userscript guide](usage-guide.md) covers script-facing parameters, return values, and examples; this reference focuses on host implementation and verification.

## CefSharp Version Compatibility

The adapter cannot support arbitrary CefSharp versions from one binary. CefSharp assemblies are strong-named, and public interfaces have changed between releases. For example, `IBrowser.GetFrame(long)` became `GetFrameByIdentifier(string)`, while `IFrame.Identifier` changed from `long` to `string`.

The adapter defaults to `121.3.70` and has source compatibility shims verified against `84.4.10`, `121.3.70`, and `151.3.240`. Build and test the adapter against every version shipped by the application:

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

The adapter's `CefSharp.Common` reference is private to its NuGet package. Consumers must reference the matching `CefSharp.WinForms`, `CefSharp.Wpf`, or `CefSharp.Common` package and allow the normal .NET Framework binding redirects. Keep the managed packages, native CEF binaries, and MonkeySharp adapter on the same version and process platform.

CI runs only on pull requests targeting `dev` or `main` and verifies `84.4.10`, `121.3.70`, and `151.3.240` for both x64 and x86. Every matrix entry restores, builds, and tests the solution, runs the JavaScript protocol tests, and executes the SmokeHost against real Chromium. The WPF Demo smoke remains an x64 check on the default `121.3.70` version.

## API Contract and Grants

MonkeySharp has one userscript contract, with both official calling forms.
The [pinned 5.5.1 experiments](compatibility/api-contract.md) determine exposure:
legacy globals require their exact declared names; modern storage, resource,
menu and tab-data methods are authorized as families. For example, granting
`GM_getValue` exposes that legacy name and the modern storage family, including
`GM.setValue`, but does not expose the undeclared `GM_setValue` global.

Missing grants, explicit `none`, unknown and info-only declarations retain
their original metadata states. All expose `GM_info` and `GM.info` in the pinned
runtime. `unsafeWindow` is absent for `none`; other declarations expose the
page global. The wrapper preserves source `"use strict"` directives; calling
style never selects strictness or execution world. Top-level `this` is the
window facade except for `none`, which has a separate object receiver as
observed in the reference. Production execution remains in the page world.

```csharp
var repository = new InMemoryUserScriptRepository();
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

`TrustedPageWorld` defaults to false. Privileged executions require verified
integrity or explicit page-world trust; info-only scripts skip bridge binding.
Legacy storage and resources consume prepared snapshots with bounded budgets.
There are no profile, alias-parsing, legacy-global or snapshot enable switches.
Dependencies require a provider regardless of API spelling; missing dependencies
skip execution with `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE`.

`IDownloadService.DownloadMode` reports the implemented native/browser/disabled
backend semantics. `IsIncognito` resolves actual context state, and
`AutomaticallyUpdatesScripts` reports actual host policy. Unknown states are
omitted from info. The Demo reports its shared normal context and manual-only
update policy. `GM.info.version` is the manager assembly version; script
version remains in `GM.info.script.version`. API availability and explicit
policy/engine reasons are recorded in `GM.info.compatibility.capabilities`.

## Resource Integrity and Update Checks

Configure installation content with `new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(dependencyProvider, resourceProvider))`, or pass the same preparer to `PersistentUserScriptRepository`. Install, edit and update await complete preparation before publishing a revision; a failed update leaves the active revision intact. All frames consume the captured revision without further content-provider calls. Dependencies share one scope and retain directive/strict semantics, with newline/semicolon boundaries between source segments.

`ResourceContent` retains original bytes, MIME, optional charset and optional host-decoded text. The host text takes precedence; otherwise charset selects the decoder, with UTF-8 as the default. Binary data URLs use the original bytes. The Demo atomically stores prepared content with the script, validates its content digests on reload, and upgrades format-2 databases with an additive table. Existing records are prepared once during initialization; unavailable content fails initialization explicitly and preserves the database. Installation providers own trusted network policy separately from GM network authorization.

The preparer defaults to a 32 MiB combined source/content budget. Resource packages have a separate 64 MiB serialized bootstrap budget and use indexed response chunks when needed. User code starts only after complete reassembly; missing, duplicate or truncated chunks fail execution. See [P1 implementation evidence](compatibility/p1-implementation.md).

`@require` and `@resource` may carry `md5`, `sha1`, `sha256`, `sha384` or `sha512` hashes in hexadecimal or standard Base64. Both `#algorithm=value` and `#algorithm-value` forms are accepted, with comma/semicolon-separated hashes; the last supported declaration is selected. The fragment is stripped from the requested and cached URL. When integrity is declared, the installation preparer, resource API, and Demo HTTP cache verify original bytes before use, and cached bytes are verified again when read. An unsupported algorithm produces a warning; malformed declarations, no usable supported hash among the declarations, and digest mismatches fail closed and prevent the resource from loading. Resources without integrity declarations load without hash verification.

The Demo manager offers a manual check-and-install action. `@updateURL` is preferred as the check source and `@downloadURL` as the download source; `@downloadURL none` disables checks. A candidate must have the same non-empty `@name` and `@namespace`, and the downloaded version must be newer than the installed one and no older than the checked version. Applying it preserves the installation key, enabled state, installation time, and GM storage while replacing source and update metadata. Checks use the Demo's separate HTTP client without browser login cookies; there is no background schedule or runtime bridge update API.

For URL rules, slash-delimited regular expressions are supported in `@include` and `@exclude`. They use culture-invariant matching with a 100 ms execution limit, and an invalid expression is a metadata error. `@match http*://host/path` matches both HTTP and HTTPS. Existing glob rules and exclusion precedence are unchanged.

## Network APIs

The CefSharp host supplies default `GM.xmlHttpRequest`, `GM.cookie`, and `GM.webRequest` services. Set `EnableDefaultNetworkServices = false` to require explicit providers, or use `UseHttpRequestService`, `UseCookieService`, and `UseWebRequestService` to replace individual defaults.

### HTTP Requests

For full parameter tables, callback contracts, and script examples, see the [HTTP request guide](usage-guide.md#http-requests).

Compatibility status is reported as Core implemented, host service required, intentional degradation or unsupported, or behavior different from Tampermonkey. The runtime executes in the CefSharp page world inside the MonkeySharp wrapper. `@sandbox`, `@unwrap`, and non-page `@inject-into` are preserved and diagnosed, but they do not create an isolated world.

Declare `@grant GM.xmlHttpRequest` or `@grant GM_xmlhttpRequest` and an `@connect` entry for every destination. Methods are case-insensitive valid HTTP tokens except `CONNECT`, `TRACE`, and `TRACK`.

Request bodies support strings, `ArrayBuffer`, typed arrays, `DataView`, `Blob`/`File`, `FormData`, `URLSearchParams`, plain objects, and arrays. Objects and arrays use JSON; `URLSearchParams` uses form encoding; `FormData` retains its generated boundary, filename, and media type. Bodies cross the bridge in 64 KiB chunks.

Supported controls include `headers`, `cookie`, `user`, `password`, `anonymous`, `overrideMimeType`, `signal`, `redirect`, `nocache`, `revalidate`, `fetch`, `timeout`, and `context`. Redirect mode defaults to `follow` and also accepts `error` and `manual`. Every redirect is rechecked against the grant, `@connect`, and host permission policy. Explicit cookies and authorization are removed on cross-origin redirects.

Compatibility headers such as `User-Agent`, `Referer`, `Origin`, and `Cookie` may be set. MonkeySharp rejects `Host`, `Content-Length`, connection and transfer-control headers, and all `Sec-*` and `Proxy-*` headers. `anonymous:true` disables stored cookies and credentials. `fetch:true` ignores timeout and progress and reports only readyState 4.

`binary: true` accepts only a string request body and maps each UTF-16 code unit to its low eight bits. Other string bodies use UTF-8. `responseType` accepts `text`, `json`, `arraybuffer`, `blob`, `document`, `xml`, and `stream`. `responseXML: true` parses the response with the page's `DOMParser`; a document response also appears as `response`. If `DOMParser` is unavailable, `responseXML` is `null` and the response is not treated as parsed XML. Non-stream responses are assembled from 64 KiB host reads. The response exposes readyState 1 through 4, status, final URL, raw repeated response headers, response data, applicable response text, response XML, and the original JavaScript context. HTTP 4xx and 5xx responses call `onload`; network, permission, serialization, and bridge failures call `onerror` with `{ error, details, code }`, using categories such as `not_supported`, `not_permitted`, `timeout`, and `not_succeeded`.

Callbacks are `onloadstart`, `onreadystatechange`, `onprogress`, `onuploadprogress`, `onload`, `onerror`, `ontimeout`, `onabort`, and `onloadend`. `GM.xmlHttpRequest` returns a Promise with `.abort()`; `GM_xmlhttpRequest` immediately returns an abort handle and reports through callbacks. `AbortSignal` and stream cancellation propagate to the host.

Total request and response body sizes are unlimited by default. Each direction stays in memory through 256 KiB and then spills to a temporary file, which is removed on every terminal path. Chunking allows total bodies to exceed a single bridge message's size limit, but each serialized chunk message still has to fit `BridgeOptions.MaxRequestBytes` or `MaxResponseBytes`, including JSON and Base64 overhead.

Only the long-running `execute` operation is exempt from `BridgeOptions.RequestTimeout`; other session operations use the ordinary timeout. All session requests, including `execute`, count toward `MaxPendingRequestsPerDocument`. The HTTP request's own `timeout` remains separate, except in `fetch` mode.

`proxy`, `cookiePartition`, synchronous requests, and `Blob`/`File` URLs return `MSP006_NOT_SUPPORTED`. Partitioned-cookie keys are browser-extension manager state that the public CefSharp request-context API does not expose. The same code is used by modern and legacy XHR input validation.

### Cookies

The default service uses the attached browser's shared CDP session and actual RequestContext. Core checks grants, host policy and script URL rules before access, then filters returned host-only/domain/path/secure scope. SameSite is mapped to CDP and read back with session/expiration state. Set checks engine acceptance and actual identity/attributes; delete selects one identity, deletes its exact domain/path and verifies absence. No reflection-based input echo or custom listeners remain.

Modern list resolves actual Cookie[]; set/delete resolve undefined. Legacy CRUD returns undefined and invokes (cookies,error) for list or (error) for mutations. The default path is the URL directory. Partition/first-party parameters are rejected before effects; newer partition support remains separate. Services created directly use new CefSharpCookieService(host), rather than a standalone RequestContext constructor. Custom ICookieService backends implement only CRUD. See the [Cookie guide](usage-guide.md#cookies) and [real Cookie evidence](compatibility/cookie-findings.md).

### Web Requests

Declare `@grant GM.webRequest` or `@grant GM_webRequest`. The default in-memory service mounts a `CefSharpWebRequestHandler`. Registration uses the Tampermonkey selector/action contract and returns a removable handle. String selectors are URL globs (or `/regexp/`); object selectors support `include`, WebExtension `match`, and `exclude` strings or arrays. Actions are `cancel`, a static HTTP(S) redirect, or `{ from, to }` dynamic replacement. Redirect targets are checked against the script's URL rules at registration and evaluation time. JavaScript callbacks never block the network thread.

Static `@webRequest` metadata is parsed and included in `GM.info.script.webRequest` for inspection, but it is deliberately not registered with the network service. Use `GM.webRequest(...)` when a script needs an active registration.

```javascript
const registration = GM.webRequest([
  { selector: "https://example.com/*", action: "cancel" },
  { selector: { match: "https://example.com/*", exclude: "https://example.com/ignore" },
    action: { from: "/old", to: "/new" } }
], (info, message, details) => {
  console.log(info.url, message, details);
});
registration.remove();
```

`GM_webRequest(rules, listener)` is the legacy alias of the same registration function. Listener calls use `(info, message, details)` and report only `cancel` or `redirect` results.

Whenever a web request service is available, including the default service, `Attach` requires an empty `RequestHandler` slot or a `CefSharpWebRequestHandlerMultiplexer`. Attach before initialization to observe the initial document; later attachment is explicitly marked. Compose an existing application handler before attaching:

```csharp
var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
requestHandlers.Add(applicationRequestHandler);
browser.RequestHandler = requestHandlers;
host.Attach(browser);
```

The adapter rejects an occupied non-multiplexer slot. Setting `EnableDefaultNetworkServices = false` omits all three default network services; supplying `UseWebRequestService` still requires the handler composition above. Existing render handlers separately require a `RenderProcessMessageHandlerMultiplexer`, as shown in the [README](../README.md#existing-browser-handlers).

## Host Service Contracts

The builder is single-use. The application owns the repository, browser, and every object supplied through `UseValueStore`, `UsePermissionPolicy`, or a `Use...Service`/`Use...Provider` method. The host owns its engine, gateway, internally created providers, default network services, and default value store. Objects passed to `AddApiProvider` transfer to the gateway and are disposed when applicable.

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

The Demo uses database format 2 and rejects older development databases without migration or deletion; back up and rebuild manually. Custom value stores must adopt the typed contract below.

The persistence implementation must preserve `ScriptKey`, installation order, and timestamps, and commit mutations atomically. Cancellation after a successful commit must complete successfully; repository change notifications follow commit. Stable keys retain the association with GM storage. See the [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md#portable-data-and-recovery) for a concrete persistence and recovery implementation.

Provider requirements:

- `IUserScriptValueStore.SetAsync` accepts an immutable `UserScriptValue`; `StoredValue.Value` exposes it and `StoredValue.Exists` alone distinguishes absence. Use `UserScriptValue.FromJson(json)`, `UserScriptValue.Undefined`, or `UserScriptValue.FromEncoded(encoded)`. This replaces the former raw-JSON storage contract; there is no legacy overload.
- Persist `EncodedValue` without dropping type information. Encodings are `u` for undefined, `btrue`/`bfalse`, `n` followed by a number or `NaN`/`Infinity`/`-Infinity`, `s` followed by literal string contents, and `o` followed by canonical object/array/null JSON. `FromEncoded` validates and canonicalizes them; a user object's property names never act as protocol markers.
- Operations for one `ScriptKey` must be linearizable. Keys may be empty strings, but script partition IDs must be non-empty. Cancellation before commit makes no change; cancellation after commit succeeds. Emit one `ValueChanged` event per actual committed change, including creation/deletion of stored undefined; identical encoded writes and deletion of absent keys emit none. `ListKeysAsync` preserves insertion order, with updates retaining position and delete/reinsert moving a key to the end.
- `IUserScriptValueStore` requires atomic `GetSnapshotAsync` (values plus partition watermark), `GetChangesAsync` (contiguous suffix after a watermark), and `ApplyAsync` (validated atomic batch with request/execution provenance). Subscribe to `ValueChanged` before taking a snapshot, then replay changes after its watermark. No per-key snapshot fallback exists. In-memory and SQLite stores retain 4096 changes per script; an expired/future watermark requests a fresh snapshot. Watermarks belong to one live store session; execution credentials expire when the host restarts.
- Bulk writes/deletes validate and commit through one `ApplyAsync`; failures publish no partial keys or events. Committed changes retain `mutationId`, `originExecutionId`, method and per-script sequence. This atomicity is a MonkeySharp store contract, not a guarantee attributed to Tampermonkey. Bulk reads consume one atomic snapshot. `ApplyAsync` returns a `UserScriptValueCommit` with the exact commit watermark even for no-op batches. The JS facade uses that acknowledgement to confirm replay before fulfilling a write Promise. Its confirmed mirror and ordered pending operations are distinct; local callbacks run synchronously and committed echoes are suppressed.
- Configure `new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(dependencyProvider, resourceProvider))` (or the persistent repository) before installing scripts. Preparation fetches and verifies all dependencies/resources, preserves dependency scope/order with statement boundaries, and atomically activates the resulting immutable revision. The default combined source/content budget is 32 MiB. Failed installation/update publishes nothing; failed updates retain the previous version. Executions never fetch installation content.
- Missing dependencies skip execution with `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE`; provider failure emits `MSR400_DEPENDENCY_RESOLUTION_FAILED`, independently of calling style.
- `IResourceProvider` returns previously authorized content for a declared resource. Resource data is limited by `BridgeOptions.MaxResourceBytes`.
- `IHttpRequestService` receives a repeatable `IUserScriptHttpBody` and observer before work begins. It reports metadata before body data, honors the optional `UserScriptHttpRequest.MaxResponseBytes` body limit (`null` means unlimited), calls `RedirectAllowed` before following redirects, and records followed URLs for Core to recheck against `@connect`. This body limit is separate from the bridge message limit `BridgeOptions.MaxResponseBytes`.
- `IUserScriptPermissionPolicy` receives the installation, frame, method, target summary, and current capabilities for every host-backed request.
- `IUserScriptWindowService` backs the exact `window.close` and `window.focus` grants. `CloseAsync` may refuse to close the last tab; omit `UseWindowService` to leave both properties unavailable. `window.onurlchange` is core-only and reports same-document `pushState`, `replaceState`, `popstate`, and `hashchange` changes.

Interaction services have distinct lifecycle contracts:

| Service entry point | Returned handle | Lifecycle members |
| --- | --- | --- |
| `INotificationService.Create` | `INotificationHandle` immediately | Unstarted handle: subscribe before `Start`; `Clicked` awaits `ResolveClick(preventDefault)`, then `Closed`/`Completion`; `Dispose` closes it. |
| `ITabService.OpenAsync` | `Task<ITabHandle>` | `TabId`, `Closed`, `OnClose`, `CloseAsync`, and `Dispose`; there is no `Completion` task. |
| `IDownloadService.CreateAsync` | `Task<IDownloadOperation>` | `Start`, `Result`, `DownloadId`, `Completion`, `Progress`, `Completed`, `Failed`, `Aborted`, `TimedOut`, `Abort`, and `Dispose`. Create must return an unstarted operation; subscribe to every event before Start. |

Use `UseDownloadServiceFactory(http => new FileDownloadService(http, selectDestination))` to share the effective CEF network service (including webRequest composition) with XHR. The builder owns factory-created disposable services. `selectDestination` is trusted host policy/UI returning an absolute `DownloadDestination` or null to cancel. Browser-mode hosts implement picker and conflict options; native-mode hosts reject them before effects. Dispose cancels accepted tasks; await `DrainAsync` before shutting down CEF. `DownloadRequest.Anonymous` and `RedirectAllowed` must be honored. Each operation emits exactly one terminal event; successful `Result` describes the committed file. Never start transfer or UI from CreateAsync.


The runtime uses handle events to deliver script callbacks; completing a `Completion` task alone does not send a terminal callback. Providers should emit one terminal event and support idempotent cleanup when the execution ends. Releasing a tab handle should release its observations; closing the user tab is a separate `CloseAsync` action.

`GM.download` immediately returns an abortable completion Promise; `GM_download` returns an abort-only handle. Both support callbacks. Completion follows final file commit, with `{url, name, loaded}` details. See the [download guide](usage-guide.md#downloads) for return values and lifecycle policy.

## Lifecycle and Diagnostics

`PrepareAsync()` prepares the attachment's shared CDP session;
`NavigateAsync(url)` waits for it before navigating. Start the browser at
`about:blank` when using this sequence. `IsLateAttach` and `IsLatePreparation`
identify initialization that already occurred. These APIs do not upgrade
userscript timing guarantees. See the [real protocol findings](compatibility/cdp-findings.md).

The adapter creates an identity per frame document and executes each `(document, frame, script, run-at)` once. `@run-at context-menu` scripts are listed by the native CefSharp context menu for the matching frame and execute a fresh invocation only after the user selects an entry. Navigation, context release and detach invalidate document capabilities and pending calls. Accepted downloads continue as host tasks with document callbacks muted; host disposal cancels them.

`GM.info` and `GM_info` are always available without a grant, under the single contract. Missing `@grant` and explicit `@grant none` expose only info; explicit grant lists include info alongside their authorized API families. Explicit `GM.info` and `GM_info` declarations remain accepted by the grant catalog (official legacy spellings are always parsed). The original declaration state and grant lists are preserved; no implicit info grant is added to metadata. Both names reference the same deep-frozen snapshot of metadata, grants, URL rules, resources, update state, handler, sandbox mode, and raw metadata text. Its versioned `compatibility` block has `contractVersion: 2` and identifies fixed, metadata-derived, mixed, and extension-owned fields, including the page-world execution boundary.

CefSharp exposes only best-effort document-start hooks. Selected document-start executions emit `MSR100_DOCUMENT_START_BEST_EFFORT`. Set `RequireGuaranteedDocumentStart = true` to skip them with `MSR101_DOCUMENT_START_UNAVAILABLE`.

`@sandbox` values other than `raw` emit `MSR212_UNSUPPORTED_SANDBOX`, and `@unwrap` emits `MSR213_UNSUPPORTED_UNWRAP`. Both declarations are preserved in metadata and `GM.info`, but execution remains in the page world and inside the MonkeySharp wrapper.

Protocol errors use stable codes `MSP001` through `MSP010` and `MSP999`. Diagnostics never include capability tokens or stored values. Configure limits by assigning a new `BridgeOptions(...)` to `CefSharpHostOptions.Bridge`:

| Option | Default | Scope |
| --- | --- | --- |
| `MaxRequestBytes` | 1 MiB | Each UTF-8 bridge request, including HTTP body chunk messages. |
| `MaxResponseBytes` | 1 MiB | Each UTF-8 bridge response, including the combined hello response and body reads. |
| `MaxResourceBytes` | 10 MiB | Each resource exposed through the resource API. Installation content has a separate `UserScriptPreparer` budget. |
| `RequestTimeout` | 30 seconds | Ordinary bridge requests; HTTP `execute` is exempt. |
| `MaxPendingRequestsPerDocument` | 64 | Concurrent bridge requests for a document, including HTTP `execute`. |
| `MaxCompatibilityBootstrapBytes` | 64 MiB | Total serialized snapshot budget. Storage and resource snapshots larger than one response are transferred in indexed chunks before user code starts. |
| `CompatibilityBootstrapTimeout` | 5 seconds | Preparation of each storage or resource snapshot. |
| `MaxReplayEntriesPerExecution` | 4096 | Recent request IDs retained to reject replays for an execution. |

Snapshot preparation failures prevent user code from starting; byte-budget errors request a host budget adjustment. Storage transfer IDs pin complete snapshots, and missing chunks or sequence gaps require reassembly/replay or a fresh snapshot:

| Diagnostic | Meaning |
| --- | --- |
| `MSC410_COMPATIBILITY_BOOTSTRAP_FAILED` | Snapshot preparation failed or timed out; the corresponding synchronous aliases are unavailable. |

## Bridge Protocol

Bridge protocol 2 is the single current protocol. Old and unknown versions are
rejected; no compatibility transport is retained. CEF frame/version adaptation,
storage encodings and persistent installation identities remain unchanged.

## Build, Package, and Verify

The [Tampermonkey contract inventory](compatibility/api-contract.md) separates
target behavior from versioned evidence. Run `scripts/compatibility/Run-Baseline.ps1`
for the clean 84/x64 baseline, including the WPF Demo. CI runs the Demo on both
84/x64 and 121/x64; its report verifies the actual assembly version and architecture.

CI's verified toolchain is Windows Server 2022, .NET SDK `9.0.315`, and Node.js `24`. These are tested versions, not a statement of minimum requirements. Use an SDK that supports `MonkeySharp.slnx`; Core tests target `net8.0`, while CefSharp tests and applications target `net462`. Real-Chromium gates require a Windows desktop session.

Use the same `CefSharpVersion` property for restore, build, test, and package commands:

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=121.3.70
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`. The x86 solution configuration excludes the x64-only Demo projects. Package Core and each adapter platform into separate directories:

Both solution platforms map Core to AnyCPU, with outputs under `bin/Release/<framework>/`. Use `-p:Platform=AnyCPU` when packing Core, including with `--no-build` to reuse solution outputs. The CefSharp adapter requires its explicit x64 or x86 platform.

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=121.3.70 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=121.3.70 -o artifacts/packages/x86
```

Run the real-Chromium gates from a Windows desktop session:

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 121.3.70
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 121.3.70
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 121.3.70
```

Use the same version value throughout validation: `-p:CefSharpVersion=<version>` for `dotnet restore/build/test/pack`, and `-CefSharpVersion <version>` for the PowerShell runners. Run the SmokeHost commands for x64 and x86 at `84.4.10`, `121.3.70`, and `151.3.240`; the Demo gate covers x64 at `84.4.10` and `121.3.70`. When switching versions in an existing checkout, first run `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<target-version>` (use the target platform) to avoid mixing incremental outputs from different CefSharp versions. The SmokeHost runner already restores, cleans and builds with the selected version, then verifies the running assembly version and process platform. Both gates use isolated profiles and `TrustedPageWorld`; they are integration checks, not security sandboxes.

### Chromium Compatibility Script Package

The SmokeHost embeds seven original `.user.js` fixtures from its `Fixtures` directory and installs them through the normal repository. Two ephemeral loopback HTTP servers provide all runtime inputs. These fixtures verify MonkeySharp's documented contract; they are not third-party scripts or differential results from Tampermonkey.

The adapter resolves the current renderer's `location.href` before matching scripts, because older CefSharp releases can expose the previous navigation's URL in the context-created callback. If that lookup fails, the document is skipped, including load fallback and context-menu execution. Document-start remains best effort.

| Result ID | Required evidence |
| --- | --- |
| `frames` | Single-key and bulk GM storage calls, including all six modern/legacy bulk entry points, typed undefined, listener return contracts, cross-instance notifications and navigation snapshots, in the main frame, a same-origin child and a child on another port; once-per-document execution, `@noframes`, child navigation and context release. |
| `spaUrl` | `pushState`, `replaceState`, hash navigation and history back; both URL callback forms, old/new URLs, host lifecycle events, grant gating and no reinjection. |
| `download` | Modern Promise/handle and legacy callbacks; actual HTTP downloads with exact file bytes, cancellation after progress, timeout, HTTP failure, one terminal callback and partial-file cleanup. |
| `cookie` | Verified CRUD, SameSite, HTTP/TLS cookie effects, exact multi-path deletion, and two real RequestContexts with isolated network state. |
| `webRequest` | Browser-native fetch cancellation and redirect, callback details, server request counts, explicit removal and navigation cleanup. |

The download service is test-only: Chromium executes GM calls and the supplied host service transfers local HTTP bytes to an isolated temporary directory. It does not test Chromium's download UI. Existing storage/XHR/notification/tab/navigation checks remain required.

Each case emits a `compatibility-case` JSON record containing `caseId`, `success`, named `checks`, and failure context (`frameId`, `documentId`, `url`, `state`, `error`). `smoke-summary.compatibility` must contain all five successful results. The runner independently rejects missing/duplicate cases, failed assertions, wrong process architecture or CefSharp assembly version, missing detach/disposal and nonzero exits. Polling has a 15-second deadline; the host has a 90-second watchdog and the runner kills a stalled process tree after 120 seconds.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.CefSharp.SmokeHost/Test-SmokeResults.ps1` to check result validation without launching a browser. Both Windows PowerShell and PowerShell 7 can run the scripts.

Logs are written to `artifacts/smoke/<version>-<platform>/` (`restore.log`, `clean.log`, `build.log` when output is present, `stdout.log`, `stderr.log`, and `results.jsonl`); `-OutputDirectory` overrides the location. CI uploads that directory as `chromium-<version>-<platform>` even on failure. Inspect failed `compatibility-case` records first, then the adjacent lifecycle, console and diagnostic records.

## Prepared execution conditions

`PreparedScriptRevision.UrlRules` compiles URL rules and repeatable run-in selectors before activation. Matching reuses immutable rules across frames, with metadata-lifetime caching. Include globs use bounded scheme/host wildcards, literal question marks and case-insensitive comparison; `/expression/` uses the documented ECMAScript subset with a 100 ms timeout. Timeouts fail closed with `MSR214_URL_RULE_TIMEOUT`. IPv6, IDN, explicit/wildcard ports, queries, fragments and escaped unreserved characters are covered by the fixed manager fixture.

`RunIn` is now a read-only selector list. `CefSharpHostOptions.IsIncognito` must report the real host-owned request context; an unknown context rejects restricted execution with `MSR211_RUN_IN_UNAVAILABLE`. Container declarations warn with `MSM083_UNSUPPORTED_CONTAINER`; unknown selector syntax is an installation error. Manual context-menu execution ignores include/exclude, while retaining match/exclude-match, noframes, privacy and trust checks. These checks do not create private contexts.

Unicode host text in `@match` is retained but does not match normalized browser hosts in the pinned manager. Use Punycode; installation reports `MSM084_UNSUPPORTED_IDN_MATCH` for this ineffective declaration. Punycode rules match canonical IDN browser URLs.
