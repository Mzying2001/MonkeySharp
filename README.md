# MonkeySharp

**English** | [中文](README.zh-CN.md)

MonkeySharp is a userscript runtime for applications that embed CefSharp. Browser-independent parsing, URL matching, repositories, storage, permissions, and API dispatch live in Core; browser lifecycle integration lives in the CefSharp adapter.

Scripts can use modern `GM.*` methods and legacy `GM_*` names. Availability depends on declared grants and host services; the [userscript guide](docs/usage-guide.md#execution-grants-and-compatibility) explains their calling conventions.

| To… | Start here |
| --- | --- |
| Run the browser and script manager | [WPF Demo](#wpf-browser-demo) |
| Embed MonkeySharp in an application | [Minimal integration](#minimal-host-integration), then the [integration reference](docs/integration-reference.md) |
| Write or port a userscript | [Userscript guide](docs/usage-guide.md) |
| Build, package, or verify the runtime | [Build and test](#build-and-test), then the [development guide](docs/development.md) |

[Documentation index](docs/README.md) · [Compatibility and evidence](docs/compatibility/README.md)

## Packages

| Package | Target frameworks | Purpose |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`, `netstandard2.0`, `net8.0` | Browser-independent runtime and host contracts. |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | Current-release CefSharp adapter for 64-bit hosts. |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | Current CefSharp adapter for 32-bit hosts. |
| `Mzying2001.MonkeySharp.CefSharp84.x64` | `net462` x64 | Adapter targeting CefSharp `84.4.10` for 64-bit hosts. |
| `Mzying2001.MonkeySharp.CefSharp84.x86` | `net462` x86 | Legacy CefSharp 84 adapter for 32-bit hosts. |

All adapter packages expose the `Mzying2001.MonkeySharp.CefSharp` assembly and namespace and depend on the shared Core package. Install one adapter matching the application's CefSharp version and platform. For source integration, reference the adapter project directly; Core is transitive:

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

Each adapter package declares an exact `CefSharp.Common` dependency. The host chooses a matching `CefSharp.WinForms`, `CefSharp.Wpf`, or other UI package and uses the same explicit `x64` or `x86` process platform. Source builds use the `CefSharpVersion` default in the [adapter project](Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj). For a published package, its exact `CefSharp.Common` dependency identifies the required engine; compile and test against the exact version you ship. See the [integration reference](docs/integration-reference.md#cefsharp-version-compatibility) for compatibility limits and binding details. Use [the package build script](docs/development.md#package) to build Core and both adapter product lines.

## Quick Start

### WPF Browser Demo

`Mzying2001.MonkeySharp.Demo` is a runnable `net462`/`x64` WPF browser with persistent scripts, background tabs, a native manager, SQLite storage, and implementations of the supported host services.

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe
```

The executable directory must be writable because its portable profile is stored under `Data/`. The Demo explicitly enables `TrustedPageWorld`: it is an integration example for trusted pages and scripts, not a hardened browser or a complete Tampermonkey replacement. See the [Demo guide](Mzying2001.MonkeySharp.Demo/README.md) for usage, recovery, service boundaries, and verification.

### Minimal Host Integration

These snippets assume an existing Windows WinForms application with a matching `CefSharp.WinForms` reference. Initialize CefSharp once at process startup, before creating any browser:

```csharp
CefSharp.CefSharpSettings.ConcurrentTaskExecution = true;
CefSharp.CefSharpSettings.WcfEnabled = false;
if (!CefSharp.Cef.Initialize(new CefSharp.WinForms.CefSettings()))
    throw new System.InvalidOperationException("CefSharp initialization failed.");
```

In an async method on the UI thread, install a script, build the host, and attach it before adding the browser to the existing `form`:

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
var browser = new CefSharp.WinForms.ChromiumWebBrowser("https://example.com/")
{
    Dock = System.Windows.Forms.DockStyle.Fill
};
host.Attach(browser);
form.Controls.Add(browser);
```

The `true` argument enables the installation. After the page loads, evaluate `document.documentElement.dataset.monkeySharp` in DevTools; it should be `"ready"`. This example uses `@grant none`. For privileged APIs, choose an execution world under the [security model](#security-model) and supply any required host services.

Keep the host and browser for the form's lifetime. Dispose the MonkeySharp host before its browser, then call `CefSharp.Cef.Shutdown()` once all browsers have been disposed. Host disposal is idempotent and cancels document sessions and pending API requests. The [SmokeHost](Mzying2001.MonkeySharp.CefSharp.SmokeHost/Program.cs) provides a complete WinForms startup and shutdown example.

### Existing Browser Handlers

CefSharp exposes one render process handler slot and one request handler slot. If the application already uses either, compose its handler before attaching MonkeySharp:

```csharp
browser.RenderProcessMessageHandler =
    new RenderProcessMessageHandlerMultiplexer(applicationRenderHandler);

var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
requestHandlers.Add(applicationRequestHandler);
browser.RequestHandler = requestHandlers;
host.Attach(browser);
```

Only compose slots that already have application handlers. `Attach` rejects occupied handler slots that have not been multiplexed. Attach before initialization to cover initial resources; late attachment is explicitly marked and cannot intercept requests already sent. The request handler requirement applies whenever a webRequest service is available, including the default service. See [request handler integration](docs/integration-reference.md#web-requests) for disabling or replacing network services.

## Security Model

The default page world and explicit DOM isolated worlds have different bridge requirements:

| Execution | Bridge requirement |
| --- | --- |
| Page world, no recognized grants / `none` / info-only | No bridge binding or trust opt-in needed. |
| Page world, recognized grants other than info | Requires `TrustedPageWorld`; only enable it for trusted pages and scripts. |
| `@sandbox DOM` or `@inject-into content` | Uses per-script isolated worlds with context-authenticated CDP transport; still requires grants, services and host authorization. |

Page-world binding is reported as `Unverified`. Its per-execution capabilities are not an extension-style isolation boundary: hostile page code can intercept them and invoke APIs granted to the script. To accept that limitation for a trusted page-world application:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

Page-world executions skipped for lack of trust emit `MSR201_BRIDGE_INTEGRITY_REQUIRED`; trusted page-world executions that need the bridge emit `MSR200_UNVERIFIED_BRIDGE`.

Installed scripts are trusted code and need an application trust policy for unknown sources. DOM isolation separates script globals and dispatch identity, but cannot expose the page's `unsafeWindow`; conflicting requirements and unsupported unwrap are rejected. Document-start remains best effort. See [execution worlds and limits](docs/compatibility/execution-worlds.md) for OOPIF, menus, CSP and BFCache.

## Metadata

Metadata covers script identity, URL selection, execution timing, grants, dependencies, resources, and update/source links. Parse errors prevent installation; warnings remain in `MetadataParseResult.Diagnostics`. See the [metadata guide](docs/usage-guide.md#metadata) for supported fields, matching rules, and examples.

`@require` and `@resource` are fetched and verified by a repository `UserScriptPreparer` before installation or update activates a revision. Executions reuse the prepared content offline and across frames. The Demo persists these revisions and offers manual script updates. Hash syntax, budgets and update rules are documented in the [integration reference](docs/integration-reference.md#resource-integrity-and-update-checks).

## GM API Support

`GM.info` and `GM_info` are always available without a grant or bridge. Other GM APIs require declared capability families and any corresponding host services. Legacy `GM_*` globals require their exact declared names.

This table summarizes availability. Parameters, return values, callbacks, and [Tampermonkey differences](docs/usage-guide.md#tampermonkey-compatibility-checklist) are covered in the [userscript guide](docs/usage-guide.md#api-index).

| API | Availability | Notes |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | Same frozen installation information; always available without a grant or bridge. |
| `GM.log` / `GM_log` | Core | Local console output and an optional host logging callback. |
| Value storage and change listeners | Core | Typed single-key/bulk storage isolated by installation; persistent when the host supplies a durable store. |
| `GM.addStyle`, `GM.addElement` and aliases | Bootstrap | Page-local implementations. |
| `GM.getResourceText`, `GM.getResourceUrl`, `GM_getResourceText`, `GM_getResourceURL` | Supported | Consume the declared resources prepared with the installation; legacy names require exact grants. |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp default | Requires `@connect`; supports credentials, abort, incremental text and binary/multipart/staged stream bodies. See [HTTP requests](docs/usage-guide.md#http-requests). |
| `GM.registerMenuCommand`, `GM.unregisterMenuCommand` and aliases | Conditional | Require `IMenuService`. |
| `GM.notification`, `GM.setClipboard`, `GM.openInTab` and aliases | Conditional | Require their corresponding host services. |
| `GM.download` / `GM_download` | Conditional | Requires `IDownloadService`; completion follows file commit. Demo shares the browser network context. See [downloads](docs/usage-guide.md#downloads). |
| `GM.getTab`, `GM.saveTab`, `GM.getTabs` and aliases | Conditional | Require `ITabStateService`. |
| `GM.cookie` / `GM_cookie` | CefSharp default | CRUD in the attached RequestContext; partition CRUD on 121/151, unavailable on 84. No change listeners or XHR request-partition selection. |
| `GM.audio` / `GM_audio` | CefSharp default | Native mute state, operations and listeners; audible omitted and partial capability reported. |
| `GM.webRequest` / `GM_webRequest` | CefSharp default | Scoped cancel/redirect rules, capture substitution, asynchronous result callbacks, and static metadata rules. No removal handle. |
| `window.close`, `window.focus` | Conditional | Require their exact grants and `IUserScriptWindowService`; the host may refuse to close the last tab. |
| `window.onurlchange` | Core | Requires the exact grant; observes `pushState`, `replaceState`, `popstate`, and `hashchange` URL changes. |
| `unsafeWindow` | Page world | Page global except with `@grant none`; unavailable in DOM isolated worlds. See [window extensions](docs/usage-guide.md#window-extensions). |

Synchronous legacy storage/resource APIs use complete bootstrap snapshots. Return values and error channels vary by API; see the [API index](docs/usage-guide.md#api-index) and [error handling](docs/usage-guide.md#errors-and-troubleshooting).

If a declared API has no provider, it is normally absent and direct bridge calls return `MSP006_NOT_SUPPORTED`. Undeclared methods return `MSP004_GRANT_DENIED`.

## Host Services

The builder uses an in-memory value store by default. Register application services with methods such as `UseValueStore` and `UseMenuService` to add persistence and capabilities.

Default XHR, Cookie, and webRequest services use the attached browser request context. Override them individually with `UseHttpRequestService`, `UseCookieService`, and `UseWebRequestService`, or set `EnableDefaultNetworkServices = false` to omit all three defaults.

See the [host service reference](docs/integration-reference.md#host-service-contracts) for the full registration example, ownership rules, and provider contracts, including the ownership transfer for `AddApiProvider`.

## Lifecycle and Diagnostics

Scripts run once per matching document/frame/lifecycle stage; `@noframes` suppresses child frames. `@run-at context-menu` executes only after a native menu selection. Navigation, context release, detach and disposal revoke old executions and pending API calls; accepted downloads can continue until completion or host disposal.

Same-document navigation does not reinject scripts. Use the granted `window.onurlchange` API or DOM observers for SPA updates; see [matching and lifecycle](docs/usage-guide.md#matching-and-lifecycle).

CefSharp provides only best-effort document-start injection. Such executions emit `MSR100_DOCUMENT_START_BEST_EFFORT`; set `RequireGuaranteedDocumentStart = true` to skip them with `MSR101_DOCUMENT_START_UNAVAILABLE`.

Subscribe to structured diagnostics:

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

See the [integration reference](docs/integration-reference.md#lifecycle-and-diagnostics) for protocol codes and bridge limits.

## Build and Test

Build on Windows using a .NET SDK that supports `MonkeySharp.slnx` and Node.js for JavaScript tests. The [development guide](docs/development.md) owns restore/build/test commands, packaging, version switching and the CI matrix.

For one clean baseline of the fixed legacy engine, including real Chromium and WPF on x64, run:

```powershell
./scripts/compatibility/Run-Baseline.ps1 -Platform x64
```

The [validation guide](docs/compatibility/validation.md) distinguishes native gates, fixed-Tampermonkey comparisons and third-party corpus results. Consult the dated matrix for recorded outcomes.

## License

MIT. See [LICENSE](LICENSE).
