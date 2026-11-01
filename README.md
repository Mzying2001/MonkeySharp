# MonkeySharp 3.0

**English** | [中文](README.zh-CN.md)

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. Browser-independent parsing, URL matching, repositories, storage, permissions, and API dispatch live in Core; browser lifecycle integration lives in the CefSharp adapter.

The default `LegacyCompatible` profile provides modern `GM.*` APIs and legacy `GM_*` aliases over an asynchronous bridge. Available capabilities depend on script grants and host services.

| To… | Start here |
| --- | --- |
| Run the browser and script manager | [WPF Demo](#wpf-browser-demo) |
| Embed MonkeySharp in an application | [Minimal integration](#minimal-host-integration), then the [integration reference](docs/integration-reference.md) |
| Write or port a userscript | [Userscript guide](docs/usage-guide.md) |
| Build, package, or verify the runtime | [Build and test](#build-and-test), then the [verification reference](docs/integration-reference.md#build-package-and-verify) |

## Packages

| Package | Target frameworks | Purpose |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`, `netstandard2.0`, `net8.0` | Browser-independent runtime and host contracts. |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | CefSharp lifecycle adapter for 64-bit hosts. |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | The same adapter for 32-bit hosts. |

Both adapter packages expose the `Mzying2001.MonkeySharp.CefSharp` assembly and namespace. For source integration, reference the adapter project directly; Core is transitive:

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

The host must also reference the matching `CefSharp.WinForms`, `CefSharp.Wpf`, or `CefSharp.Common` package; the adapter's `CefSharp.Common` dependency is private. Build the application and adapter for the same explicit `x64` or `x86` process platform. The adapter defaults to CefSharp `121.3.70`; compile and test against the exact version you ship. See the [integration reference](docs/integration-reference.md#cefsharp-version-compatibility) for tested versions and binding details.

## Quick Start

### WPF Browser Demo

`Mzying2001.MonkeySharp.Demo` is a runnable `net462`/`x64` WPF browser with persistent scripts, background tabs, a native manager, SQLite storage, and implementations of the supported host services.

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
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

The `true` argument enables the installation. After the page loads, evaluate `document.documentElement.dataset.monkeySharp` in DevTools; it should be `"ready"`. This example uses `@grant none`; grants other than `none` or info require the [trust opt-in](#security-model) and any corresponding host services.

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

Only compose slots that already have application handlers. `Attach` rejects an initialized browser or an occupied render handler that has not been multiplexed. The request handler requirement applies whenever a webRequest service is available, including the default service. See [request handler integration](docs/integration-reference.md#web-requests) for disabling or replacing network services.

## Security Model

CefSharp's public APIs execute bootstrap code in the page's main world, so MonkeySharp reports this bridge as `Unverified`.

- The default policy runs scripts with no explicit API grants, `@grant none`, or only info grants. Scripts with explicit grants other than `none` or info are suppressed unless the application opts into `TrustedPageWorld`.
- `TrustedPageWorld` enables explicitly granted APIs only when the application trusts both the page and installed scripts.
- Per-execution capabilities prevent unrelated callers from impersonating an installation, but they are not an extension-style isolation boundary. Hostile page code can intercept a capability and invoke APIs granted to that script.
- Installed userscripts are trusted code. Applications need their own review, signature, or trust policy for unknown sources.

Enable privileged APIs only after accepting that limitation:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

Default-mode suppressions emit `MSR201_BRIDGE_INTEGRITY_REQUIRED`; trusted-mode executions that require the bridge emit `MSR200_UNVERIFIED_BRIDGE`. Info-only executions need no bridge binding or handshake.

Scripts execute in the page world inside the MonkeySharp wrapper. `@sandbox`, `@unwrap`, `@run-in`, and non-page `@inject-into` declarations do not create an isolated world; `unsafeWindow` does not provide an extension-style security boundary.

## Metadata

Metadata covers script identity, URL selection, execution timing, grants, dependencies, resources, and update/source links. Parse errors prevent installation; warnings remain in `MetadataParseResult.Diagnostics`. See the [metadata guide](docs/usage-guide.md#metadata) for supported fields, matching rules, and examples.

`@require` and `@resource` need host providers and support integrity hashes. The Demo also offers manual script updates. Hash syntax, validation, and update rules are documented in the [integration reference](docs/integration-reference.md#resource-integrity-and-update-checks).

Static `@webRequest` metadata is validated and included in `GM.info`, but does not install interception rules. Use `GM.webRequest(...)` for active registrations.

## GM API Support

`GM.info` and `GM_info` are always available without a grant or bridge, in both compatibility profiles and even when legacy globals are disabled. Missing `@grant` and explicit `@grant none` expose only info; other APIs require their individual grants and any corresponding host providers.

This table summarizes availability. Parameters, return values, callbacks, and [Tampermonkey differences](docs/usage-guide.md#tampermonkey-compatibility-checklist) are covered in the [userscript guide](docs/usage-guide.md#api-index).

| API | Availability | Notes |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | Same frozen installation information; always available without a grant or bridge. |
| `GM.log` / `GM_log` | Core | Delivered to the builder's logging callback. |
| Value storage and change listeners | Core | Single-key and bulk `getValues`/`setValues`/`deleteValues` APIs with `GM_*` aliases; Typed value storage (including stored `undefined`) isolated by stable `ScriptKey`, with committed change notifications. |
| `GM.addStyle`, `GM.addElement` and aliases | Bootstrap | Page-local implementations. |
| `GM.getResourceText`, `GM.getResourceURL` and aliases | Conditional | Require `IResourceProvider` and a declared `@resource`. |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp default | Requires `@connect`; supports redirects, credentials, progress, abort, and binary/multipart bodies. See [HTTP requests](docs/usage-guide.md#http-requests) for response types and limitations. |
| `GM.registerMenuCommand`, `GM.unregisterMenuCommand` and aliases | Conditional | Require `IMenuService`. |
| `GM.notification`, `GM.setClipboard`, `GM.openInTab` and aliases | Conditional | Require their corresponding host services. |
| `GM.download` / `GM_download` | Conditional | Requires `IDownloadService`. Modern calls resolve a start handle and ignore callback fields; progress/completion callbacks require the legacy facade. See [downloads](docs/usage-guide.md#downloads). |
| `GM.getTab`, `GM.saveTab`, `GM.getTabs` and aliases | Conditional | Require `ITabStateService`. |
| `GM.cookie` / `GM_cookie` | CefSharp default | Structured access to the attached browser request context. |
| `GM.webRequest` / `GM_webRequest` | CefSharp default | Runtime registrations with removable handles and cancel/redirect result callbacks. |
| `window.close`, `window.focus` | Conditional | Require their exact grants and `IUserScriptWindowService`; the host may refuse to close the last tab. |
| `window.onurlchange` | Core | Requires the exact grant; observes `pushState`, `replaceState`, `popstate`, and `hashchange` URL changes. |
| `unsafeWindow` | Trusted page world only | Exposed only for its exact grant. |

Legacy aliases other than `GM_info` require `LegacyCompatible` and enabled legacy globals. Synchronous storage/resource aliases also need complete bootstrap snapshots. Return values and error-reporting channels vary by API; some legacy failures are logged to the page console. See [compatibility profiles](docs/integration-reference.md#compatibility-profiles) and [error handling](docs/usage-guide.md#errors-and-troubleshooting).

If a declared API has no provider, it is normally absent and direct bridge calls return `MSP006_NOT_SUPPORTED`. Undeclared methods return `MSP004_GRANT_DENIED`.

## Host Services

The builder uses an in-memory value store by default. Register application services with methods such as `UseValueStore`, `UseDependencyProvider`, `UseResourceProvider`, and `UseMenuService` to add persistence and capabilities.

Default XHR, Cookie, and webRequest services use the attached browser request context. Override them individually with `UseHttpRequestService`, `UseCookieService`, and `UseWebRequestService`, or set `EnableDefaultNetworkServices = false` to omit all three defaults.

See the [host service reference](docs/integration-reference.md#host-service-contracts) for the full registration example, ownership rules, and provider contracts, including the ownership transfer for `AddApiProvider`.

## Lifecycle and Diagnostics

Each `(document, frame, script, run-at)` combination executes once. `@run-at context-menu` scripts are added to the native CefSharp context menu for matching frames and create a fresh execution only after the user selects an entry. Navigation, context release, detach, and disposal invalidate capabilities and cancel pending work. `@noframes` suppresses child-frame execution.

Same-document URL changes from `history.pushState`, `history.replaceState`, `popstate`, and `hashchange` do not reinject scripts. Scripts granted `window.onurlchange` can subscribe through the property or `addEventListener("urlchange", ...)`. Existing `GM.info` snapshots remain fixed; subsequent executions use the current URL.

CefSharp provides only best-effort document-start injection. Such executions emit `MSR100_DOCUMENT_START_BEST_EFFORT`; set `RequireGuaranteedDocumentStart = true` to skip them with `MSR101_DOCUMENT_START_UNAVAILABLE`.

Subscribe to structured diagnostics:

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

See the [integration reference](docs/integration-reference.md#lifecycle-and-diagnostics) for protocol codes and bridge limits.

## Migrating from v1

Version 3 has source-breaking host service contracts and replaces `Injector`, `JScript`, `IDataStore`, and the WCF bridge. Storage keys must be migrated explicitly when retaining v1 data. See the [migration notes](docs/integration-reference.md#migration-notes) for the type mapping and service changes.

## Build and Test

Build on Windows with a .NET SDK that supports `MonkeySharp.slnx` and Node.js for JavaScript tests. CI uses .NET SDK `9.0.315` and Node.js `24`; these are tested versions, not minimum requirements. Core tests target `net8.0`; CefSharp tests and applications target `net462`.

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=121.3.70
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`; the x86 configuration excludes the x64-only Demo projects. Pass the same `CefSharpVersion` property to every command when testing another supported version. Packaging and real-Chromium E2E commands are in the [integration reference](docs/integration-reference.md#build-package-and-verify).

CI covers CefSharp `84.4.10`, `121.3.70`, and `151.3.240` on x64 and x86 with .NET/JavaScript tests and real-Chromium SmokeHost gates. The WPF Demo gate runs on x64 with the default version. See [compatibility gate details](docs/integration-reference.md#chromium-compatibility-script-package) for test coverage and CI logs.

## License

MIT. See [LICENSE](LICENSE).
