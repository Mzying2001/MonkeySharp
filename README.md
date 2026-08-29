# MonkeySharp

MonkeySharp 2 is a userscript runtime for applications that embed CefSharp. It separates browser-independent script parsing, matching, storage, permissions, and API dispatch from the CefSharp lifecycle adapter.

Version 2 is a breaking rewrite. The v1 `Injector`, `JScript`, `IDataStore`, synchronous `GM_*` functions, and WCF bridge were removed.

## Packages

| Package | Target frameworks | Purpose |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`, `netstandard2.0`, `net8.0` | Metadata, matching, repository, engine, storage, permissions, bridge, and host service contracts. No CefSharp dependency. |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | CefSharp 121.3.70 frame lifecycle, asynchronous binding, script execution, and diagnostics. |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | The same adapter compiled explicitly for 32-bit processes. |

Both adapter packages expose the `Mzying2001.MonkeySharp.CefSharp` assembly and namespace. Separate package IDs prevent x64 and x86 artifacts with the same version from overwriting each other.

The packages are versioned `2.0.0`. Until they are published, add a project reference from an application beside this repository; Core is referenced transitively:

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

Build the consuming application and project reference with the same `Platform=x64` or `Platform=x86` as its process. CefSharp does not support this adapter as `AnyCPU`.

## Minimal Setup

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

// Use the equivalent delayed-creation option for your CefSharp UI package.
var browser = new CefSharp.WinForms.ChromiumWebBrowser(
    "about:blank",
    automaticallyCreateBrowser: false);
host.Attach(browser);
browser.CreateBrowser();
```

The `true` argument enables the installation. This minimal example deliberately uses `@grant none`; scripts with host-backed grants also require the registrations and trust opt-in described below.

Dispose the host when the browser closes. `Dispose` is idempotent and cancels document sessions and pending API requests.

```csharp
host.Dispose();
browser.Dispose();
```

Do not enable `CefSharpSettings.WcfEnabled`. MonkeySharp uses only CefSharp asynchronous JavaScript binding.

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

Every affected document emits `MSR200_UNVERIFIED_BRIDGE`. The adapter cannot be configured to claim `Verified` integrity.

## Metadata

The strict parser requires the first non-empty line to be `// ==UserScript==` and a matching `// ==/UserScript==` line. Metadata errors prevent installation; warnings are retained in `MetadataParseResult.Diagnostics`.

Supported fields:

- Identity and display: `@name`, localized `@name:locale`, `@namespace`, `@version`, `@description`, `@author`, `@license`, `@icon`/`@iconURL`.
- Selection: `@match`, `@include`, `@exclude`, `@exclude-match`, `@noframes`.
- Timing: `@run-at document-start|document-body|document-end|document-idle`, plus preserved `@run-in` and `@inject-into` values.
- Capabilities: `@grant`, `@connect`, `@require`, and `@resource`.
- Source metadata: `@downloadURL`, `@updateURL`, `@homepageURL`, and `@supportURL`.

Exclusions win over positive rules. At least one `@match` or `@include` must match. URL fragments are ignored, hosts are IDN-normalized, explicit ports are checked, and `*.example.com` matches subdomains but not the bare domain.

Missing `@grant` means `@grant none`. Grants are exact and case-sensitive. Unknown grants produce a warning and are never exposed.

## GM API Support

Only `GM.*` is provided. Legacy synchronous `GM_*` globals do not exist. Except for `GM.info` and the local value-listener ID, methods return real promises.

| API | Availability | Notes |
| --- | --- | --- |
| `GM.info` | Core | Frozen installation information; requires its exact grant. |
| `GM.log` | Core | Delivered to the builder's `LogTo` callback. |
| `GM.getValue`, `setValue`, `deleteValue`, `listValues` | Core | Asynchronous canonical JSON storage, isolated by stable `ScriptKey`. |
| `GM.addValueChangeListener`, `removeValueChangeListener` | Core | Notifications go only to active executions of the same installation. |
| `GM.addStyle`, `addElement` | Bootstrap | Page-local implementation; still requires the exact grant. |
| `GM.getResourceText`, `getResourceURL` | Conditional | Requires `IResourceProvider` and a declared `@resource`. |
| `GM.xmlHttpRequest` | Conditional | Requires `IHttpRequestService`; initial and redirected URLs must satisfy `@connect`. Supports load/error/timeout callbacks and progress notifications. |
| `GM.registerMenuCommand`, `unregisterMenuCommand` | Conditional | Requires `IMenuService`; callbacks use authenticated notifications. |
| `GM.notification` | Conditional | Requires `INotificationService`. |
| `GM.setClipboard` | Conditional | Requires `IClipboardService`. |
| `GM.openInTab` | Conditional | Requires `ITabService`. |
| `GM.download` | Conditional | Requires `IDownloadService`. |
| `GM.getTab`, `saveTab`, `getTabs` | Conditional | Requires `ITabStateService`. |
| `unsafeWindow` | Trusted page world only | Bound only for the exact `@grant unsafeWindow`. |
| `GM.cookie`, `GM.webRequest` | Not implemented | Requires dedicated Chromium semantics; no placeholder functions are exposed. |

If a script directly requests a declared API that has no provider, protocol error `MSP006_NOT_SUPPORTED` is returned. Capability discovery prevents the bootstrap from exposing that method in normal use.

In the default unverified mode, a script with host-backed grants is skipped entirely, so it cannot inspect `GM`. In `TrustedPageWorld`, an exact grant without a registered capability leaves that method undefined; a direct bridge request still receives `MSP006_NOT_SUPPORTED`. An undeclared method receives `MSP004_GRANT_DENIED`.

## Host Services

Register only the capabilities the application implements:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseDependencyProvider(cachedDependencyProvider)
    .UseResourceProvider(resourceProvider)
    .UseHttpRequestService(httpService)
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

The builder is single-use. Ownership is explicit:

| Object | Owner and disposal rule |
| --- | --- |
| `CefSharpUserScriptHost` | Application; dispose it before the browser. It owns its engine, gateway, internally created API providers, and default in-memory store. |
| `IUserScriptRepository` and CefSharp browser | Application; the host never disposes them. |
| Objects passed to `UseValueStore`, `UsePermissionPolicy`, or any `Use...Service` / `Use...Provider` method | Application; the host never disposes these service objects. This includes `IResourceProvider` and `IUserScriptDependencyProvider`. |
| Objects passed to `AddApiProvider` | Ownership transfers to the gateway; it calls `Dispose` when the object implements `IDisposable`. |
| Menu registrations returned by `IMenuService` | Internal host provider; released on unregister, execution end, or host disposal. |

Important service requirements:

- `IUserScriptValueStore` operations for one `ScriptKey` must be linearizable. Cancellation before commit makes no change; cancellation after commit still completes successfully and emits one notification.
- `IUserScriptDependencyProvider` is responsible for trusted download, caching, HTTPS policy, and optional hash/signature validation of `@require`. `ResourceScriptSourceResolver` preserves declaration order and enforces the configured byte limit.
- `@require` is resolved before the main source is planned and does not use a GM grant. A resolution failure skips that invocation and emits `MSR400_DEPENDENCY_RESOLUTION_FAILED`.
- `IResourceProvider` must return previously authorized content for the named declaration. Resource and HTTP responses are capped by `BridgeOptions.MaxResourceBytes`.
- `IHttpRequestService` must respect `UserScriptHttpRequest.MaxResponseBytes` and call `RedirectAllowed` before following every redirect. It must report followed redirects in `UserScriptHttpResponse.RedirectUrls`; Core rechecks that chain and the final URL against `@connect`.
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

Default bridge limits are 1 MiB per request, 1 MiB per response, 10 MiB per resource, 30 seconds per request, and 64 pending requests per document. Configure them through `CefSharpHostOptions.Bridge`.

## Migrating from v1

| v1 | v2 |
| --- | --- |
| `JScript` / random `ScriptId` | Repository-owned `UserScriptInstallation` / stable `ScriptKey` |
| `JScriptMeta` dictionary | Immutable `UserScriptMetadata` plus parser diagnostics |
| `IList<JScript>` on `InjectorBase` | `IUserScriptRepository` snapshots and change events |
| `IDataStore` / `MemDataStore` | `IUserScriptValueStore` / `InMemoryUserScriptValueStore` |
| `Injector.AttachBrowser` | Build `CefSharpUserScriptHost`, then `Attach` before browser initialization |
| Synchronous Messenger/WCF | Versioned JSON protocol through an asynchronous string binding |
| `GM_getValue` and other `GM_*` | Promise-based `GM.getValue` and `GM.*` only |
| `ForceUseStrict` and `with(window)` | Strict `Function` scope with explicit `GM`, `unsafeWindow`, and `window` parameters |
| `IScriptVerifier` character scanner | Strict metadata diagnostics; Chromium reports JavaScript syntax/runtime errors |

There is no default compatibility package. Migrate storage keys explicitly if existing v1 data must be retained.
Legacy grant aliases are not translated: update metadata to the exact, case-sensitive `GM.*` names shown in the support table.

## Build and Test

The repository pins exactly .NET SDK 9.0.315 and CefSharp 121.3.70.

```powershell
dotnet restore MonkeySharp.sln -p:Platform=x64
dotnet build MonkeySharp.sln -c Release -p:Platform=x64 --no-restore
dotnet test MonkeySharp.sln -c Release -p:Platform=x64 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`. Pack platform-specific CefSharp artifacts into separate directories. The resulting adapter files are `Mzying2001.MonkeySharp.CefSharp.x64.2.0.0.nupkg` and `Mzying2001.MonkeySharp.CefSharp.x86.2.0.0.nupkg`:

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -o artifacts/packages/x86
```

## License

MIT. See [LICENSE](LICENSE).
