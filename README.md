# MonkeySharp 3.0

**English** | [中文](README.zh-CN.md)

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. Browser-independent parsing, URL matching, repositories, storage, permissions, and API dispatch live in Core; browser lifecycle integration lives in the CefSharp adapter.

Version 3 has source-breaking host service contracts. Its default `LegacyCompatible` profile supports the common v1 userscript surface over an asynchronous bridge. The old `Injector`, `JScript`, `IDataStore`, and WCF bridge remain removed.

## Packages

| Package | Target frameworks | Purpose |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`, `netstandard2.0`, `net8.0` | Browser-independent runtime and host contracts. |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | CefSharp lifecycle adapter for 64-bit hosts. |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | The same adapter for 32-bit hosts. |

Both adapter packages expose the `Mzying2001.MonkeySharp.CefSharp` assembly and namespace. Until version `3.0.0` packages are published, reference the adapter project directly; Core is transitive:

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

Build the application and adapter for the same explicit `x64` or `x86` process platform. The adapter defaults to CefSharp `121.3.70` and is source-tested with `84.4.10`, `121.3.70`, and `151.3.240`; compile and test against the exact version you ship. See the [integration reference](docs/integration-reference.md#cefsharp-version-compatibility) for version selection and binding details.

## Quick Start

### WPF Browser Demo

`Mzying2001.MonkeySharp.Demo` is a runnable `net462`/`x64` WPF browser with persistent scripts, background tabs, a native manager, SQLite storage, and implementations of the supported host services.

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
```

The executable directory must be writable because its portable profile is stored under `Data/`. The Demo explicitly enables `TrustedPageWorld`: it is an integration example for trusted pages and scripts, not a hardened browser or a complete Tampermonkey replacement. See the [Demo guide](Mzying2001.MonkeySharp.Demo/README.md) for usage, recovery, service boundaries, and verification.

### Minimal Host Integration

Install a script, build the host, attach it before the browser is initialized, and then add the browser to the UI:

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
var browser = new CefSharp.WinForms.ChromiumWebBrowser("about:blank");
host.Attach(browser);
form.Controls.Add(browser);
```

The `true` argument enables the installation. This example uses `@grant none`; host-backed grants require the trust opt-in and providers described below. Default XHR, Cookie, and webRequest services follow the attached browser request context. They may be disabled or replaced through the builder.

Before creating the first browser, enable concurrent Task binding once for the process. Do not enable CefSharp WCF:

```csharp
CefSharpSettings.ConcurrentTaskExecution = true;
```

Dispose the MonkeySharp host before its browser. Disposal is idempotent and cancels document sessions and pending API requests.

### Existing Render Handler

CefSharp exposes one `IRenderProcessMessageHandler` slot. Compose an existing handler before attaching MonkeySharp:

```csharp
browser.RenderProcessMessageHandler =
    new RenderProcessMessageHandlerMultiplexer(applicationHandler);
host.Attach(browser);
```

`Attach` rejects an initialized browser or an occupied handler that has not been multiplexed.

## Security Model

CefSharp's public APIs execute bootstrap code in the page's main world, so MonkeySharp reports this bridge as `Unverified`.

- The default policy runs scripts with no explicit API grants. A script with no `@grant` receives no `GM` facade; an explicit `@grant none` script receives only `GM.info`/`GM_info`. Scripts with any other explicit grants are suppressed unless the application opts into `TrustedPageWorld`.
- `TrustedPageWorld` enables explicitly granted APIs only when the application trusts both the page and installed scripts.
- Per-execution capabilities prevent unrelated callers from impersonating an installation, but they are not an extension-style isolation boundary. Hostile page code can intercept a capability and invoke APIs granted to that script.
- Installed userscripts are trusted code. Applications need their own review, signature, or trust policy for unknown sources.

Enable privileged APIs only after accepting that limitation:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

Default-mode suppressions emit `MSR201_BRIDGE_INTEGRITY_REQUIRED`; trusted-mode executions emit `MSR200_UNVERIFIED_BRIDGE` and the adapter cannot claim verified integrity.

## Metadata

The default parser accepts a bounded preamble before `// ==UserScript==`; `ModernStrict` requires the first non-empty line to be the header. Errors prevent installation, while warnings remain in `MetadataParseResult.Diagnostics`.

Supported fields include:

- Identity: `@name`, localized names, `@namespace`, `@version`, `@description`, `@author`, `@license`, `@copyright`, and icons including `@icon64`/`@icon64URL`.
- Selection: `@match`, `@include`, `@exclude`, `@exclude-match`, and `@noframes`. Slash-delimited `@include`/`@exclude` regular expressions run with a 100 ms limit; invalid expressions are metadata errors. `@match http*://...` covers HTTP and HTTPS.
- Execution: `@run-at`, `@run-in`, `@inject-into`, `@sandbox`, and `@unwrap`. Unsupported sandbox modes and wrapper removal emit `MSR212_UNSUPPORTED_SANDBOX` and `MSR213_UNSUPPORTED_UNWRAP`; execution remains in the page world and inside the MonkeySharp wrapper.
- Capabilities and assets: `@grant`, `@connect`, `@require`, and `@resource`.
- Source links: `@downloadURL`, `@updateURL`, `@homepageURL`, `@website`, `@source`, and `@supportURL`.
- Disclosure and request metadata: `@antifeature` (including localized forms) and validated static `@webRequest` JSON rules.

Exclusions override positive rules. URL fragments are ignored, hosts are IDN-normalized, explicit ports are checked, and `*.example.com` does not match the bare domain. Grant state distinguishes a missing `@grant`, explicit `@grant none`, and an explicit grant list. Recognized legacy aliases are normalized, while unknown grants remain case-sensitive and are never exposed.

`@require` and `@resource` accept `md5` and `sha256` integrity hashes in hexadecimal or standard Base64, using `#algorithm=<digest>` or `#algorithm-<digest>` syntax; comma- or semicolon-separated declarations use the last supported hash. The URL fragment is removed before requests and cache lookup, and the original bytes are checked before use and again when cached content is read. A resource with only unsupported algorithms, malformed declarations, or a digest mismatch is rejected; unsupported algorithm names also produce a warning.

The Demo's script manager can manually check for and install an update. `@updateURL` takes priority as the check source, while `@downloadURL` takes priority for the source being installed; `@downloadURL none` disables checking. Updates require matching non-empty name and namespace and a higher downloaded version. Checks do not run in the background or inherit browser login cookies, and the runtime bridge exposes no update API.

`GM.info` and `GM_info` expose a frozen snapshot containing script metadata, declared and normalized grants, URL rules, resources, update/source links, handler, sandbox mode, and the raw metadata header. Static `@webRequest` entries are validated and included in this snapshot; they are not installed into the host network interceptor. Use the runtime `GM.webRequest(...)` API for registrations that affect requests.

## GM API Support

Canonical `GM.*` APIs and legacy aliases are exposed only when the exact grant and required host provider are present.

| API | Availability | Notes |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | Frozen installation information. |
| `GM.log` / `GM_log` | Core | Delivered to the builder's logging callback. |
| `GM.getValue`, `GM.setValue`, `GM.deleteValue`, `GM.listValues` and aliases | Core | JSON storage isolated by stable `ScriptKey`. |
| `GM.addValueChangeListener`, `GM.removeValueChangeListener` and aliases | Core | Notifications for active executions of the same installation. |
| `GM.addStyle`, `GM.addElement` and aliases | Bootstrap | Page-local implementations. |
| `GM.getResourceText`, `GM.getResourceURL` and aliases | Conditional | Require `IResourceProvider` and a declared `@resource`. |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp default | XHR with redirects, credentials, progress, abort, binary/multipart bodies, and text/JSON/binary/blob/stream responses. Requires `@connect`. |
| `GM.registerMenuCommand`, `GM.unregisterMenuCommand` and aliases | Conditional | Require `IMenuService`. |
| `GM.notification`, `GM.setClipboard`, `GM.openInTab`, `GM.download` and aliases | Conditional | Require their corresponding host services. Notifications support the legacy four-argument overload and the `highlight`, `silent`, and `timeout` options. |
| `GM.getTab`, `GM.saveTab`, `GM.getTabs` and aliases | Conditional | Require `ITabStateService`. |
| `GM.cookie` / `GM_cookie` | CefSharp default | Structured access to the attached browser request context. |
| `GM.webRequest` / `GM_webRequest` | CefSharp default | Tampermonkey selector/action registrations with removable handles and cancel/redirect result callbacks. |
| `window.close` | Conditional | Requires the exact `window.close` grant and `IUserScriptWindowService`; the host may refuse to close the last tab. |
| `window.focus` | Conditional | Requires the exact `window.focus` grant and `IUserScriptWindowService`. |
| `window.onurlchange` | Core | Requires the exact grant; observes `pushState`, `replaceState`, `popstate`, and `hashchange` URL changes. |
| `unsafeWindow` | Trusted page world only | Exposed only for its exact grant. |

Modern storage and resource reads return Promises; their legacy aliases use bounded synchronous bootstrap snapshots. Legacy callback APIs return their handle or ID immediately and report callback failures as diagnostics.

If a declared API has no provider, it is normally absent and direct bridge calls return `MSP006_NOT_SUPPORTED`. Undeclared methods return `MSP004_GRANT_DENIED`. `UseWindowService` is required for `window.close` and `window.focus`; without it those properties remain unavailable even when granted. Detailed XHR, Cookie, and webRequest behavior is documented in the [network API reference](docs/integration-reference.md#network-apis).

## Host Services

Register application-owned capabilities on the builder. Default network services may be kept or overridden:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseDependencyProvider(cachedDependencyProvider)
    .UseResourceProvider(resourceProvider)
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .UseWindowService(windowService)
    .UseCookieService(cookieService)          // optional default override
    .UseWebRequestService(webRequestService) // optional default override
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

The application owns the repository, browser, and supplied services. The host owns its engine, gateway, and internally created providers. Detailed ownership, consistency, dependency, redirect, and streaming requirements are in the [host service reference](docs/integration-reference.md#host-service-contracts).

## Lifecycle and Diagnostics

Each `(document, frame, script, run-at)` combination executes once. Navigation, context release, detach, and disposal invalidate capabilities and cancel pending work. `@noframes` suppresses child-frame execution.

The adapter reports same-document URL changes from `history.pushState`, `history.replaceState`, `popstate`, and `hashchange` as lifecycle events. Scripts granted `window.onurlchange` receive those events through the `window.onurlchange` property or `addEventListener("urlchange", ...)`; the current URL is reflected in subsequent matching and `GM.info` snapshots.

CefSharp provides only best-effort document-start injection. Such executions emit `MSR100_DOCUMENT_START_BEST_EFFORT`; set `RequireGuaranteedDocumentStart = true` to skip them with `MSR101_DOCUMENT_START_UNAVAILABLE`.

Subscribe to structured diagnostics:

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

See the [integration reference](docs/integration-reference.md#lifecycle-and-diagnostics) for protocol codes and bridge limits.

## Migrating from v1

| v1 | 3.0 |
| --- | --- |
| `JScript` / random `ScriptId` | Repository-owned `UserScriptInstallation` / stable `ScriptKey` |
| `JScriptMeta` dictionary | Immutable `UserScriptMetadata` plus parser diagnostics |
| `IList<JScript>` on `InjectorBase` | `IUserScriptRepository` snapshots and change events |
| `IDataStore` / `MemDataStore` | `IUserScriptValueStore` / `InMemoryUserScriptValueStore` |
| `Injector.AttachBrowser` | Build a host, then call `Attach` before browser initialization |
| Synchronous Messenger/WCF | Versioned JSON over asynchronous string binding |
| `GM_*` globals | Default `LegacyCompatible` aliases over the current bridge |
| `IScriptVerifier` character scanner | Metadata diagnostics plus Chromium syntax/runtime reporting |

Storage keys must be migrated explicitly when retaining v1 data. Custom host services also require source changes; see the [migration notes](docs/integration-reference.md#migration-notes).

## Build and Test

The repository uses .NET SDK `9.0.315` and defaults to CefSharp `121.3.70`. CI covers `84.4.10`, `121.3.70`, and `151.3.240` on both x64 and x86, including .NET and JavaScript tests and a real-Chromium SmokeHost gate for every combination. The WPF Demo smoke gate runs on x64 with the default version.

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=121.3.70
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`; the x86 configuration excludes the x64-only Demo projects. Pass the same `CefSharpVersion` property to every command when testing another supported version. Packaging and real-Chromium E2E commands are in the [integration reference](docs/integration-reference.md#build-package-and-verify).

## License

MIT. See [LICENSE](LICENSE).
