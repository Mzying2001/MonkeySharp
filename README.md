# MonkeySharp 3.0

**English** | [中文](README.zh-CN.md)

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. Browser-independent parsing, URL matching, repositories, storage, permissions, and API dispatch live in Core; browser lifecycle integration lives in the CefSharp adapter.

One userscript contract provides both official calling forms over an asynchronous bridge. Legacy names follow exact declarations; modern API families follow the pinned reference evidence and actual host services.

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

Private tabs use an independent in-memory context, temporary permissions and copied GM baselines with no private writes to normal storage. See [private browsing](docs/usage-guide.md#private-browsing).

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

Only compose slots that already have application handlers. `Attach` rejects occupied handler slots that have not been multiplexed. Attach before initialization to cover initial resources; late attachment is explicitly marked and cannot intercept requests already sent. The request handler requirement applies whenever a webRequest service is available, including the default service. See [request handler integration](docs/integration-reference.md#web-request) for disabling or replacing network services.

## Security Model

Page-world bootstrap uses CefSharp binding and is reported as `Unverified`; per-script DOM worlds use a context-authenticated CDP transport.

- Page-world execution with host-backed grants requires `TrustedPageWorld`; no-grant/none/info scripts can run without that opt-in. Explicit DOM worlds use the verified isolated transport and still require metadata grants and host authorization.
- `TrustedPageWorld` enables explicitly granted APIs only when the application trusts both the page and installed scripts.
- Per-execution capabilities prevent unrelated callers from impersonating an installation, but they are not an extension-style isolation boundary. Hostile page code can intercept a capability and invoke APIs granted to that script.
- Installed userscripts are trusted code. Applications need their own review, signature, or trust policy for unknown sources.

Enable page-world privileged APIs only after accepting that limitation:

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

Default-mode suppressions emit `MSR201_BRIDGE_INTEGRITY_REQUIRED`; trusted-mode executions that require the bridge emit `MSR200_UNVERIFIED_BRIDGE`. Info-only executions need no bridge binding or handshake.

`@sandbox DOM` (or `@inject-into content`) selects a per-installation isolated world with context-authenticated CDP dispatch. Raw/default and Chromium JavaScript access use the page world and the trust policy above. DOM isolation cannot expose the actual page unsafeWindow; conflicting requirements and unsupported unwrap are rejected. Full GM document-start is best effort, and OOPIF/BFCache limits are explicit. See [execution evidence](docs/compatibility/execution-worlds.md).

## Metadata

Metadata covers script identity, URL selection, execution timing, grants, dependencies, resources, and update/source links. Parse errors prevent installation; warnings remain in `MetadataParseResult.Diagnostics`. See the [metadata guide](docs/usage-guide.md#metadata) for supported fields, matching rules, and examples.

`@require` and `@resource` are fetched and verified by a repository `UserScriptPreparer` before installation or update activates a revision. Executions reuse the prepared content offline and across frames. The Demo persists these revisions and offers manual script updates. Hash syntax, budgets and update rules are documented in the [integration reference](docs/integration-reference.md#resource-integrity-and-update-checks).

Static `@webRequest` rules are compiled at installation and activated before prepared navigation. Dynamic `GM.webRequest(...)` replaces this execution's rules; `[]` revokes them. See the [P2 scope and evidence](docs/compatibility/p2-implementation.md).

## GM API Support

`GM.info` and `GM_info` are always available without a grant or bridge, under the single contract. Missing `@grant` and explicit `@grant none` expose only info; other APIs require their declared capability families and any corresponding host providers.

This table summarizes availability. Parameters, return values, callbacks, and [Tampermonkey differences](docs/usage-guide.md#tampermonkey-compatibility-checklist) are covered in the [userscript guide](docs/usage-guide.md#api-index).

| API | Availability | Notes |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | Same frozen installation information; always available without a grant or bridge. |
| `GM.log` / `GM_log` | Core | Delivered to the builder's logging callback. |
| Value storage and change listeners | Core | Single-key and bulk `getValues`/`setValues`/`deleteValues` APIs with `GM_*` aliases; Typed value storage (including stored `undefined`) isolated by stable `ScriptKey`, with synchronous local listeners and deduplicated remote commit notifications. |
| `GM.addStyle`, `GM.addElement` and aliases | Bootstrap | Page-local implementations. |
| `GM.getResourceText`, `GM.getResourceUrl`, `GM_getResourceText`, `GM_getResourceURL` | Supported | Consume the declared resources prepared with the installation; legacy names require exact grants. |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp default | Requires `@connect`; supports redirects, credentials, progress, abort, and binary/multipart bodies. See [HTTP requests](docs/usage-guide.md#http-requests) for response types and limitations. |
| `GM.registerMenuCommand`, `GM.unregisterMenuCommand` and aliases | Conditional | Require `IMenuService`. |
| `GM.notification`, `GM.setClipboard`, `GM.openInTab` and aliases | Conditional | Require their corresponding host services. |
| `GM.download` / `GM_download` | Conditional | Requires `IDownloadService`. Modern calls immediately return an abortable completion Promise; both forms support callbacks. Demo shares the browser network context. See [downloads](docs/usage-guide.md#downloads). |
| `GM.getTab`, `GM.saveTab`, `GM.getTabs` and aliases | Conditional | Require `ITabStateService`. |
| `GM.cookie` / `GM_cookie` | CefSharp default | Verified CRUD through shared CDP in the attached RequestContext, with SameSite and exact deletion. No custom listeners or partition support. |
| `GM.audio` / `GM_audio` | CefSharp default | Native mute state, operations and listeners; audible omitted and partial capability reported. |
| `GM.webRequest` / `GM_webRequest` | CefSharp default | Scoped cancel/redirect rules, capture substitution, asynchronous result callbacks, and static metadata rules. No removal handle. |
| `window.close`, `window.focus` | Conditional | Require their exact grants and `IUserScriptWindowService`; the host may refuse to close the last tab. |
| `window.onurlchange` | Core | Requires the exact grant; observes `pushState`, `replaceState`, `popstate`, and `hashchange` URL changes. |
| `unsafeWindow` | Trusted page world only | Exposed only for its exact grant. |

Legacy globals other than `GM_info` require exact declared names. Synchronous storage/resource aliases also need complete bootstrap snapshots. Return values and error-reporting channels vary by API; some legacy failures are logged to the page console. See [API contract](docs/integration-reference.md#api-contract-and-grants) and [error handling](docs/usage-guide.md#errors-and-troubleshooting).

If a declared API has no provider, it is normally absent and direct bridge calls return `MSP006_NOT_SUPPORTED`. Undeclared methods return `MSP004_GRANT_DENIED`.

Partitioned Cookie CRUD is verified on 121/151; 84 and request cookiePartition controls reject explicitly. See [partition scope](docs/usage-guide.md#partitioned-cookie-scope).

## Host Services

The builder uses an in-memory value store by default. Register application services with methods such as `UseValueStore` and `UseMenuService` to add persistence and capabilities.

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

CI covers CefSharp `84.4.10`, `121.3.70`, and `151.3.240` on x64 and x86 with .NET/JavaScript tests and real-Chromium SmokeHost gates. The WPF Demo gates run on x64 with all three versions. See [compatibility gate details](docs/integration-reference.md#chromium-compatibility-script-package) and the [P2 delivery record](docs/compatibility/p2-delivery.md).

## License

MIT. See [LICENSE](LICENSE).
