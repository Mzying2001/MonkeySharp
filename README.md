# MonkeySharp 3.0

**English** | [中文](README.zh-CN.md)

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. It separates browser-independent script parsing, matching, storage, permissions, and API dispatch from the CefSharp lifecycle adapter.

Version 3 is a breaking release of the host service contracts. Its default `LegacyCompatible` profile provides the common v1 userscript surface over the version 1 asynchronous bridge. The v1 `Injector`, `JScript`, `IDataStore`, and WCF bridge remain removed; no synchronous WCF transport is restored.

## Packages

| Package | Target frameworks | Purpose |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`, `netstandard2.0`, `net8.0` | Metadata, matching, repository, engine, storage, permissions, bridge, and host service contracts. No CefSharp dependency. |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | CefSharp frame lifecycle, asynchronous binding, script execution, and diagnostics. The adapter is compiled against the selected `CefSharpVersion` (default `121.3.70`). |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | The same adapter compiled explicitly for 32-bit processes. |

Both adapter packages expose the `Mzying2001.MonkeySharp.CefSharp` assembly and namespace. Separate package IDs prevent x64 and x86 artifacts with the same version from overwriting each other.

The packages are versioned `3.0.0`. Until they are published, add a project reference from an application beside this repository; Core is referenced transitively:

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

Build the consuming application and project reference with the same `Platform=x64` or `Platform=x86` as its process. CefSharp does not support this adapter as `AnyCPU`.

### CefSharp Version Compatibility

The adapter cannot support an arbitrary CefSharp version from one binary. CefSharp assemblies are strong-named and their public interfaces have changed between releases (for example, `IBrowser.GetFrame(long)` became `GetFrameByIdentifier(string)`, and `IFrame.Identifier` changed from `long` to `string`). A package compiled against one version therefore must not be assumed to run with every other version.

The adapter project keeps a deterministic default (`121.3.70`) but lets the host select the exact CefSharp version it uses. Set `CefSharpVersion` before restoring/building the adapter (the property also flows through a `ProjectReference`):

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

The source adapter has compatibility shims for the old and new frame lookup/identifier APIs and is verified against `84.4.10`, `121.3.70`, and `151.3.240`. This is a tested compatibility set, not a promise that every future CefSharp release is binary-compatible; compile and test against each version you ship.

The adapter's `CefSharp.Common` reference is private to its NuGet package so it cannot force a conflicting CefSharp version into the application. A package consumer must reference the matching `CefSharp.WinForms`, `CefSharp.Wpf`, or `CefSharp.Common` package itself, and for .NET Framework applications must allow the normal CefSharp binding redirects. Keep all CefSharp packages, native CEF binaries, and the MonkeySharp adapter on the same version.

## Minimal Setup

### WPF Browser Demo

`Mzying2001.MonkeySharp.Demo` is a runnable `net462`/`x64` WPF browser using `CefSharp.Wpf`, `CommunityToolkit.Mvvm`, and SQLite. It provides persistent userscripts, real background tabs, a native script editor/manager, installation permission review, script menus, notifications, clipboard writes, tab handles, downloads, resources, and the adapter's default XHR/Cookie/webRequest services. Each retained browser control has its own MonkeySharp host; tabs share the repository, GM value store, and Chromium request context.

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
```

The executable directory must be writable. Data stays under its `Data/` directory, not the shell's working directory: `BrowserCache/`, `UserScripts/`, `Dependencies/`, `Downloads/`, `Logs/`, and `monkeysharp.db`. A profile lock prevents two instances from writing the same data. The x86 solution configuration continues to build the libraries/tests and excludes the x64-only Demo projects.

**The Demo explicitly enables `TrustedPageWorld`. It is a trusted-environment integration example, not a secure general-purpose browser or a complete Tampermonkey replacement.** The warning is also displayed in the browser and script manager. Read `Mzying2001.MonkeySharp.Demo/README.md` for usage, recovery, the implemented host-service boundaries, and manual verification scenarios.

```powershell
dotnet test Mzying2001.MonkeySharp.Demo.Tests/Mzying2001.MonkeySharp.Demo.Tests.csproj -c Release -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
```

The WPF E2E gate uses an isolated profile and a loopback HTTP server. It verifies real background tabs, shared storage/cookies, resources, XHR, webRequest, menus, notification clicks, downloads, frames, navigation, script toggles, and disposal. It does not replace the existing WinForms SmokeHost gate.

### Minimal Host Integration

Install a script into a repository, build the host, attach it before the browser is initialized, and then create the browser.

```csharp
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Repository;
using System.Threading;

var repository = new InMemoryUserScriptRepository();
await repository.InstallAsync(@"
// ==UserScript==
// @name Add a marker
// @match https://example.com/*
// @grant none
// @run-at document-end
// ==/UserScript==
document.documentElement.dataset.monkeySharp = 'ready';
", "application://bundled/marker.user.js", true, CancellationToken.None);

var host = new CefSharpUserScriptHostBuilder(repository).Build();

// Attach before the control handle is created.
var browser = new CefSharp.WinForms.ChromiumWebBrowser("about:blank");
host.Attach(browser);
// Add the control to the form after Attach. CefSharp 121 creates the native
// browser when the WinForms control handle is created.
form.Controls.Add(browser);
```

The `true` argument enables the installation. This minimal example deliberately uses `@grant none`; scripts with host-backed grants also require the registrations and trust opt-in described below.

`CefSharpUserScriptHostBuilder` enables default `GM.xmlHttpRequest`, `GM.webRequest`, and `GM.cookie` services. Set `EnableDefaultNetworkServices = false` to retain the explicit-injection behavior, or call the corresponding `Use...Service` method to override one service. Default services follow the currently attached browser request context and are disposed with the host; application-supplied services remain application-owned.

Dispose the host when the browser closes. `Dispose` is idempotent and cancels document sessions and pending API requests.

```csharp
host.Dispose();
browser.Dispose();
```

Do not enable `CefSharpSettings.WcfEnabled`. MonkeySharp uses only CefSharp asynchronous JavaScript binding.
Before creating the first browser, enable CefSharp's concurrent Task binding once for the process:

```csharp
CefSharpSettings.ConcurrentTaskExecution = true;
```

### Compatibility Profiles

`CefSharpHostOptions.Compatibility` defaults to `LegacyCompatible`. It accepts legacy `@grant` aliases such as `GM_getValue`, exposes the corresponding `GM_*` globals with non-strict wrapper semantics, and bootstraps bounded synchronous storage/resource mirrors. Legacy callback facades (`GM_xmlhttpRequest`, menu registration, and tab callbacks) share the same authenticated bridge operations as their `GM.*` counterparts.

Use `ModernStrict` when scripts must retain strict wrapper semantics and Promise-only API names:

```csharp
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Parsing;

var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions
    {
        Compatibility = new UserScriptCompatibilityOptions
        {
            Profile = UserScriptCompatibilityProfile.ModernStrict
        }
    })
    .Build();
```

The profile is selected by the host for execution. Metadata parsing is selected when the repository is created, so already-installed metadata is not reinterpreted at runtime:

```csharp
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Parsing;

var parser = new UserScriptMetadataParser(
    new UserScriptMetadataParserOptions
    {
        Profile = UserScriptCompatibilityProfile.ModernStrict,
        AllowHeaderPreamble = false
    });
var repository = new InMemoryUserScriptRepository(parser);
```

### Existing Render Handler

CefSharp exposes one `IRenderProcessMessageHandler` slot. Compose handlers explicitly before attaching MonkeySharp:

```csharp
browser.RenderProcessMessageHandler =
    new RenderProcessMessageHandlerMultiplexer(applicationHandler);
host.Attach(browser);
```

`Attach` rejects an initialized browser or a non-multiplexed occupied handler. It never silently replaces an application handler.

## Security Model

CefSharp 121.3.70 public APIs execute bootstrap code in the page's main world. MonkeySharp therefore reports the CefSharp bridge as `Unverified`.

- The default policy runs `@grant none` scripts but suppresses scripts that request host-backed GM APIs.
- `TrustedPageWorld` is an explicit compatibility mode for pages and installed scripts that the application trusts.
- A 256-bit per-execution capability prevents callers that do not know the token from impersonating another installation. In a main-world page, this is not a Chromium extension-style isolation boundary.
- A hostile page can wrap JavaScript built-ins and CefSharp binding functions before bootstrap runs, capture the capability, and then invoke every API granted to that script. The token must not be treated as protection from the page itself.
- Installed userscripts can modify the page and are trusted code. Character scanning is not used or described as a sandbox.
- Unknown-source scripts require an application-level signature, review, or trust policy.

Enable host APIs only after accepting this limitation:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions
    {
        TrustedPageWorld = true
    })
    .Build();
```

Each privileged script execution in an affected document emits `MSR200_UNVERIFIED_BRIDGE`. The adapter cannot be configured to claim `Verified` integrity.

## Metadata

The default legacy-compatible parser accepts a BOM, blank lines, license comments, and bounded tool comments before `// ==UserScript==`. The scan is limited to 128 lines and 64 KiB; metadata errors still prevent installation and warnings are retained in `MetadataParseResult.Diagnostics`. `ModernStrict` requires the first non-empty line to be the header and disables preamble scanning.

Supported fields:

- Identity and display: `@name`, localized `@name:locale`, `@namespace`, `@version`, `@description`, `@author`, `@license`, `@icon`/`@iconURL`.
- Selection: `@match`, `@include`, `@exclude`, `@exclude-match`, `@noframes`.
- Timing: `@run-at document-start|document-body|document-end|document-idle`, plus preserved `@run-in` and `@inject-into` values.
- Capabilities: `@grant`, `@connect`, `@require`, and `@resource`.
- Source metadata: `@downloadURL`, `@updateURL`, `@homepageURL`, and `@supportURL`.

Exclusions win over positive rules. At least one `@match` or `@include` must match. URL fragments are ignored, hosts are IDN-normalized, explicit ports are checked, and `*.example.com` matches subdomains but not the bare domain.

Missing `@grant` means `@grant none`. Known legacy aliases are normalized into canonical capabilities while their original spelling remains in `UserScriptMetadata.DeclaredGrants` and `GM.info`. Unknown grants remain case-sensitive, produce a warning, and are never exposed.

## GM API Support

Both canonical `GM.*` methods and the legacy facade are available when the corresponding grant and host provider are present. `GM.getValue`, `GM.listValues`, `GM.getResourceText`, and `GM.getResourceURL` remain Promise APIs; their `GM_*` aliases read the execution's bounded bootstrap mirror synchronously. Callback-style legacy APIs return their legacy handle/ID immediately and report callback failures as diagnostics.

| API | Availability | Notes |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | Frozen installation information; requires its exact grant. `GM_info` preserves declared grant spelling. |
| `GM.log` / `GM_log` | Core | Delivered to the builder's `LogTo` callback. |
| `GM.getValue`, `GM.setValue`, `GM.deleteValue`, `GM.listValues` / `GM_getValue`, `GM_setValue`, `GM_deleteValue`, `GM_listValues` | Core | Canonical JSON storage isolated by stable `ScriptKey`; modern methods are asynchronous, legacy mirror reads are synchronous and writes are queued in order. |
| `GM.addValueChangeListener`, `GM.removeValueChangeListener` / `GM_addValueChangeListener`, `GM_removeValueChangeListener` | Core | Notifications go only to active executions of the same installation. |
| `GM.addStyle`, `GM.addElement` / `GM_addStyle`, `GM_addElement` | Bootstrap | Page-local implementation; still requires the exact grant. |
| `GM.getResourceText`, `GM.getResourceURL` / `GM_getResourceText`, `GM_getResourceURL` | Conditional | Requires `IResourceProvider` and a declared `@resource`; legacy reads use the bounded bootstrap snapshot. |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | Conditional | CefSharp hosts create a default `CefSharpHttpRequestService`; applications can override it with `UseHttpRequestService`. Supports binary and multipart request bodies, redirects, credentials, progress, abort, and text/JSON/binary/blob/stream responses. Initial and redirected URLs must satisfy `@connect`. |
| `GM.registerMenuCommand`, `GM.unregisterMenuCommand` / `GM_registerMenuCommand`, `GM_unregisterMenuCommand` | Conditional | Requires `IMenuService`; the legacy form allocates its ID synchronously and registers asynchronously. |
| `GM.notification` / `GM_notification` | Conditional | Requires `INotificationService`; legacy callbacks receive click/done lifecycle notifications. |
| `GM.setClipboard` / `GM_setClipboard` | Conditional | Requires `IClipboardService`. |
| `GM.openInTab` / `GM_openInTab` | Conditional | Requires `ITabService`; legacy `close()` calls the host and exposes `closed`. |
| `GM.download` / `GM_download` | Conditional | Requires `IDownloadService`; legacy callbacks receive progress, success, failure, and abort events. |
| `GM.getTab`, `GM.saveTab`, `GM.getTabs` / `GM_getTab`, `GM_saveTab`, `GM_getTabs` | Conditional | Requires `ITabStateService`; legacy tab methods use callbacks. |
| `unsafeWindow` | Trusted page world only | Bound only for the exact `@grant unsafeWindow`. |
| `GM.cookie` / `GM_cookie` | Conditional | CefSharp hosts create a context-backed `CefSharpCookieService`; applications can override it with `UseCookieService`. List/set/delete and service-originated change listeners use the browser request context. |
| `GM.webRequest` / `GM_webRequest` | Conditional | CefSharp hosts create an `InMemoryWebRequestService` and attach `CefSharpWebRequestHandler`; applications can override it with `UseWebRequestService`. Rules cover request/response/auth phases, while callbacks observe events. |

If a script directly requests a declared API that has no provider, protocol error `MSP006_NOT_SUPPORTED` is returned. Capability discovery prevents the bootstrap from exposing that method in normal use.

In the default unverified mode, a script with host-backed grants is skipped entirely, so it cannot inspect `GM`. In `TrustedPageWorld`, an exact grant without a registered capability leaves that method undefined; a direct bridge request still receives `MSP006_NOT_SUPPORTED`. An undeclared method receives `MSP004_GRANT_DENIED`.

### HTTP Request API

Declare `@grant GM.xmlHttpRequest` or `@grant GM_xmlhttpRequest` and an `@connect` entry for every destination. `details.url` accepts a string or `URL`. Methods are case-insensitive and otherwise unrestricted when they are valid HTTP tokens, except for `CONNECT`, `TRACE`, and `TRACK`.

Request bodies support strings, `ArrayBuffer`, typed arrays and `DataView`, `Blob`/`File`, `FormData`, `URLSearchParams`, plain objects, and arrays. Plain objects and arrays use JSON; `URLSearchParams` uses form encoding; `FormData` retains its generated multipart boundary, file name, and media type. Request data crosses the bridge in 64 KiB chunks instead of one complete Base64 message.

The supported controls are `headers`, `cookie`, `user`, `password`, `anonymous`, `overrideMimeType`, `signal`, `redirect`, `nocache`, `revalidate`, `fetch`, `timeout`, and `context`. Redirect mode is `follow` by default and also accepts `error` and `manual`. Follow mode applies Chromium's 301/302/303 method conversion and preserves the method and body for 307/308. Every redirect target is checked again against the grant, `@connect`, and the host permission policy; explicit cookies and authorization are removed on a cross-origin redirect.

Compatibility headers including `User-Agent`, `Referer`, `Origin`, and `Cookie` may be set. MonkeySharp rejects `Host`, `Content-Length`, connection and transfer-control headers, and all `Sec-*` and `Proxy-*` headers. `anonymous:true` disables the CefSharp request context's stored cookies and credentials. `fetch:true` ignores timeout and progress and reports only readyState 4, matching Tampermonkey's documented fetch-mode restrictions.

`responseType` accepts `text` (the default), `json`, `arraybuffer`, `blob`, and `stream`. A stream response exposes a cancelable `ReadableStream`; the other types are assembled from 64 KiB host reads. The response object contains `readyState` 1 through 4, `status`, `statusText`, `finalUrl`, `responseHeaders`, `response`, `responseText` where applicable, and the original JavaScript `context` reference. `responseHeaders` is the raw CRLF-separated header string and retains repeated fields; the earlier non-standard `responseBase64` field is removed. HTTP 4xx and 5xx responses call `onload`; network, permission, body serialization, and bridge failures call `onerror`.

Lifecycle callbacks are `onloadstart`, `onreadystatechange`, `onprogress`, `onuploadprogress` (or `upload.onprogress`), `onload`, `onerror`, `ontimeout`, `onabort`, and `onloadend`. Callbacks run with the stable response object as `this`; an exception in one callback is reported to the console but does not change the terminal result or prevent cleanup. `GM.xmlHttpRequest` returns a Promise with `.abort()`, while `GM_xmlhttpRequest` immediately returns an abort handle and delivers its result through callbacks. An `AbortSignal` and stream cancellation both propagate to the host request.

Request and response sizes are unlimited by default. Each direction stays in memory through 256 KiB and then spills to a temporary file that is deleted on completion, abort, failure, navigation, or host disposal. HTTP body transfer and the long-running `execute` operation are independent of the bridge's general 1 MiB message and 30-second request limits; `details.timeout`, explicit abort, and page teardown still cancel the request.

`proxy`, `cookiePartition`, and a `Blob`/`File` used as the URL fail with `MSP006_NOT_SUPPORTED`. Partitioned-cookie keys are browser-extension manager state that CefSharp's public request-context API does not expose. CefSharp `84.4.10` uses the legacy request/post-data factories and 121/151 use the current factories; all three builds share the same documented HTTP behavior. Run real-browser tests on every exact CefSharp version shipped by the application.

### Cookie API

Declare `@grant GM.cookie` (or the legacy alias `GM_cookie`). CefSharp hosts use the currently attached browser request context automatically; an explicit `UseCookieService` still takes precedence. URLs must be absolute HTTP or HTTPS URLs; domain, path, secure, SameSite, and expiration fields are validated by Core. `GM.xmlHttpRequest` has its own compatibility-oriented explicit Cookie header support; `GM.cookie` remains the structured browser-store API, and `httpOnly` visibility is decided by that service rather than `document.cookie`.

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();

// For an application-owned implementation, call UseCookieService(...) here.
```

Modern calls return Promises:

```javascript
const cookies = await GM.cookie.list({ url: location.href });
await GM.cookie.set({ url: location.href, name: "session", value: "ready", path: "/" });
await GM.cookie.delete({ url: location.href, name: "session" });
const listenerId = GM.cookie.addListener({ url: location.href }, change => console.log(change.cause));
```

`GM_cookie.list/set/delete` use callbacks and `addListener/removeListener` use numeric IDs. Listener registrations are released when the execution, document, frame, or host is disposed.

### Web Request API

Declare `@grant GM.webRequest` (or `GM_webRequest`). CefSharp automatically uses an `InMemoryWebRequestService` and mounts a `CefSharpWebRequestHandler`; call `UseWebRequestService(...)` to supply an application-owned implementation. Rules are validated when registered and rechecked against the permission policy for each request. Blocking, redirect, header modification, and auth decisions are pre-registered host rules; a userscript callback never runs on the network thread and cannot delay a request.

```javascript
const ruleId = await GM.webRequest.addRule({
  id: "strip-tracking",
  phase: "OnBeforeRequest",
  priority: 100,
  filter: { urlPatterns: ["https://example.com/*"], resourceTypes: ["Script"] },
  action: { kind: "ModifyRequestHeaders", headers: { "X-Userscript": "1" } }
});
const listenerId = GM.webRequest.addListener({}, event => console.log(event.phase, event.url));
```

`GM_webRequest(rules, listener)` is the legacy registration form and returns a removable handle. Rules are ordered by priority descending and registration order ascending; block and redirect terminate evaluation, and later header rules replace earlier values. The CefSharp adapter rejects an occupied non-multiplexer request handler instead of silently replacing it.

## Host Services

Register application-owned capabilities (network services are provided by default unless disabled):

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseDependencyProvider(cachedDependencyProvider)
    .UseResourceProvider(resourceProvider)
    .UseHttpRequestService(httpService) // optional override of the CefSharp default
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .UseCookieService(cookieService) // optional override of the CefSharp default
    .UseWebRequestService(webRequestService) // optional override of the in-memory default
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();

// Set EnableDefaultNetworkServices = false to require explicit network services.
```

The builder is single-use. Ownership is explicit:

| Object | Owner and disposal rule |
| --- | --- |
| `CefSharpUserScriptHost` | Application; dispose it before the browser. It owns its engine, gateway, internally created API providers, default network services, and default in-memory store. |
| `IUserScriptRepository` and CefSharp browser | Application; the host never disposes them. |
| Objects passed to `UseValueStore`, `UsePermissionPolicy`, or any `Use...Service` / `Use...Provider` method | Application; the host never disposes these service objects. This includes `IResourceProvider` and `IUserScriptDependencyProvider`. |
| Objects passed to `AddApiProvider` | Ownership transfers to the gateway; it calls `Dispose` when the object implements `IDisposable`. |
| Menu registrations returned by `IMenuService` | Internal host provider; released on unregister, execution end, or host disposal. |

Important service requirements:

- `IUserScriptValueStore` operations for one `ScriptKey` must be linearizable. Cancellation before commit makes no change; cancellation after commit still completes successfully and emits one notification.
- `IUserScriptDependencyProvider` is responsible for trusted download, caching, HTTPS policy, and optional hash/signature validation of `@require`. `ResourceScriptSourceResolver` preserves declaration order and enforces the configured byte limit.
- `@require` is resolved before the main source is planned when an `IUserScriptDependencyProvider` is configured, and it does not use a GM grant. In `LegacyCompatible`, declaring `@require` without a dependency provider skips that invocation with `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE`; a configured provider failure emits `MSR400_DEPENDENCY_RESOLUTION_FAILED`. In `ModernStrict`, configure a provider to load declared dependencies; without one, the installed main source is used as-is.
- `IResourceProvider` must return previously authorized content for the named declaration. Resources are capped by `BridgeOptions.MaxResourceBytes`; HTTP request and response bodies are not subject to that resource limit.
- `IHttpRequestService` receives a repeatable `IUserScriptHttpBody` and an observer before execution starts. It must respect `UserScriptHttpRequest.MaxResponseBytes` when non-null, report response metadata before body data, and call `RedirectAllowed` before following every redirect. It must report followed redirects in `UserScriptHttpResponse.RedirectUrls`; Core rechecks that chain and the final URL against `@connect`.
- `IUserScriptPermissionPolicy` receives the installation, frame, method, target summary, and current host capabilities for every host-backed request.

## Lifecycle and Diagnostics

The CefSharp adapter creates an identity for every frame document and executes each `(document, frame, script, run-at)` once. Main frames and subframes are both observed; `@noframes` suppresses child execution. Navigation, context release, detach, and disposal invalidate capabilities and cancel pending work.

Public CefSharp hooks provide only `BestEffortDocumentStart`: injection starts after the renderer context callback and may occur after page inline scripts. Each selected document-start execution emits `MSR100_DOCUMENT_START_BEST_EFFORT` before script execution. Set `RequireGuaranteedDocumentStart = true` to skip such scripts with `MSR101_DOCUMENT_START_UNAVAILABLE`.

Subscribe to structured diagnostics rather than relying on swallowed exceptions:

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

Protocol errors use stable codes `MSP001` through `MSP010` and `MSP999`. Diagnostics never include capability tokens or stored values.

Default bridge limits are 1 MiB per request, 1 MiB per response, 10 MiB per resource, 30 seconds per ordinary request, and 64 pending requests per document. Configure them through `CefSharpHostOptions.Bridge`. XHR body chunks remain below the message limits, and XHR `execute` is exempt from the ordinary request timeout because it has its own lifecycle and `details.timeout`.

## Migrating to 3.0

| v1 | 3.0 |
| --- | --- |
| `JScript` / random `ScriptId` | Repository-owned `UserScriptInstallation` / stable `ScriptKey` |
| `JScriptMeta` dictionary | Immutable `UserScriptMetadata` plus parser diagnostics |
| `IList<JScript>` on `InjectorBase` | `IUserScriptRepository` snapshots and change events |
| `IDataStore` / `MemDataStore` | `IUserScriptValueStore` / `InMemoryUserScriptValueStore` |
| `Injector.AttachBrowser` | Build `CefSharpUserScriptHost`, then `Attach` before browser initialization |
| Synchronous Messenger/WCF | Versioned JSON protocol through an asynchronous string binding |
| `GM_getValue` and other `GM_*` | `LegacyCompatible` aliases over the version 1 bridge; storage/resource reads use a synchronous execution snapshot and writes are ordered asynchronously |
| One-shot `GM_xmlhttpRequest` payload/result | Lifecycle session using `create`, `appendBody`, `execute`, `readBody`, `abort`, and `release`; update custom HTTP services to the observer/operation contract |
| `ForceUseStrict` and `with(window)` | Strict `Function` scope with explicit `GM`, `unsafeWindow`, and `window` parameters |
| `IScriptVerifier` character scanner | Strict metadata diagnostics; Chromium reports JavaScript syntax/runtime errors |

Compatibility is built into the default profile; there is no separate transport or WCF package. Migrate storage keys explicitly if existing v1 data must be retained. Applications that require strict language semantics can select `ModernStrict` and update metadata grants to canonical `GM.*` names.

The 3.0 host contract changes are source-breaking for service implementations. `IHttpRequestService.SendAsync` receives `IUserScriptHttpObserver` before starting and returns `IHttpRequestOperation`; request bodies are repeatable streams, response data is delivered incrementally, and redirect authorization is asynchronous. Existing adapters should wrap their old task/future in the operation, attach the observer before work begins, and forward response start, data, progress, completion, and abort. Notification, tab, and download services likewise return lifecycle handles/operations with completion and cancellation members. `ITabStateService` accepts JSON objects only. The new `UseCookieService` and `UseWebRequestService` builder methods are optional and do not change hosts that do not register those providers.

## Build and Test

The repository uses .NET SDK 9.0.315 and defaults to CefSharp 121.3.70. To build and test another supported CefSharp version, pass the same `CefSharpVersion` property to restore, build, test, and pack:

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 --no-build
# Example: -p:CefSharpVersion=151.3.240 on every command above
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`. Pack platform-specific CefSharp artifacts into separate directories. The resulting adapter files are `Mzying2001.MonkeySharp.CefSharp.x64.3.0.0.nupkg` and `Mzying2001.MonkeySharp.CefSharp.x86.3.0.0.nupkg`:

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -o artifacts/packages/x86
```

The WinForms smoke host is a real Chromium compatibility gate. It requires a Windows desktop session with CefSharp native binaries; `Run-E2E.ps1` builds, runs, parses the JSON Lines output, and fails unless storage, XHR (including credentials, redirects, binary and large bodies, repeated headers, streaming, timeout, and abort), notification, tab, download, cookie, webRequest, navigation, frame lifecycle, and final disposal checks all pass:

```powershell
dotnet build Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x64
dotnet run --project Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x64 --no-build

dotnet build Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x86
dotnet run --project Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x86 --no-build

./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86
```

Pass `-CefSharpVersion 151.3.240` to the script to validate an alternate managed/native CefSharp set. Its JSON-lines output contains a `smoke-summary` record with individual API booleans and a `final-disposal` record. The smoke host uses `TrustedPageWorld`; it is an integration check, not a security sandbox. Page-world code can observe and invoke granted capabilities.

Advanced manager-private behavior is intentionally outside the compatibility promise: browser-extension-only cookie partitioning, manager UI state, download shelf presentation, notification persistence, and request interception APIs that require waiting on JavaScript are host-specific. Implementations must expose only the documented provider contract and return `MSP006_NOT_SUPPORTED` when a capability is absent.

## License

MIT. See [LICENSE](LICENSE).
