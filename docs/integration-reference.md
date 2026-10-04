# MonkeySharp Integration Reference

**English** | [中文](integration-reference.zh-CN.md)

This reference collects the compatibility, network API, host service, migration, and verification details that are useful while implementing or maintaining a MonkeySharp host. Start with the repository [README](../README.md) for installation and minimal integration.

## CefSharp Version Compatibility

The adapter cannot support arbitrary CefSharp versions from one binary. CefSharp assemblies are strong-named, and public interfaces have changed between releases. For example, `IBrowser.GetFrame(long)` became `GetFrameByIdentifier(string)`, while `IFrame.Identifier` changed from `long` to `string`.

The adapter defaults to `121.3.70` and has source compatibility shims verified against `84.4.10`, `121.3.70`, and `151.3.240`. Build and test the adapter against every version shipped by the application:

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

The adapter's `CefSharp.Common` reference is private to its NuGet package. Consumers must reference the matching `CefSharp.WinForms`, `CefSharp.Wpf`, or `CefSharp.Common` package and allow the normal .NET Framework binding redirects. Keep the managed packages, native CEF binaries, and MonkeySharp adapter on the same version and process platform.

CI verifies `84.4.10`, `121.3.70`, and `151.3.240` for both x64 and x86. Every matrix entry restores, builds, and tests the solution, runs the JavaScript protocol tests, and executes the SmokeHost against real Chromium. The WPF Demo smoke remains an x64 check on the default `121.3.70` version.

## Compatibility Profiles

`CefSharpHostOptions.Compatibility` defaults to `LegacyCompatible`. It accepts legacy grants such as `GM_getValue`, exposes `GM_*` globals with their compatibility semantics, and creates bounded synchronous storage and resource mirrors. Legacy callback facades use the same authenticated bridge operations as their `GM.*` counterparts.

Use `ModernStrict` for strict wrapper semantics and Promise-only API names:

```csharp
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

Execution compatibility is selected by the host. Metadata compatibility is selected when the repository parser is created, so use the same profile for both:

```csharp
var parser = new UserScriptMetadataParser(
    new UserScriptMetadataParserOptions
    {
        Profile = UserScriptCompatibilityProfile.ModernStrict,
        AllowHeaderPreamble = false
    });
var repository = new InMemoryUserScriptRepository(parser);
```

## Resource Integrity and Update Checks

`@require` and `@resource` may carry `md5` or `sha256` hashes in hexadecimal or standard Base64. Both `#algorithm=value` and `#algorithm-value` forms are accepted, with comma/semicolon-separated hashes; the last supported declaration is selected. The fragment is stripped from the requested and cached URL. The Core resolver, resource API, and Demo HTTP cache verify original bytes before use, and cached bytes are verified again when read. An unsupported algorithm produces a warning; no usable supported hash, malformed declarations, and digest mismatches fail closed and prevent the resource from loading.

The Demo manager offers a manual check-and-install action. `@updateURL` is preferred as the check source and `@downloadURL` as the download source; `@downloadURL none` disables checks. A candidate must have the same non-empty `@name` and `@namespace`, and the downloaded version must be newer than the installed one and no older than the checked version. Applying it preserves the installation key, enabled state, installation time, and GM storage while replacing source and update metadata. Checks use the Demo's separate HTTP client without browser login cookies; there is no background schedule or runtime bridge update API.

For URL rules, slash-delimited regular expressions are supported in `@include` and `@exclude`. They use culture-invariant matching with a 100 ms execution limit, and an invalid expression is a metadata error. `@match http*://host/path` matches both HTTP and HTTPS. Existing glob rules and exclusion precedence are unchanged.

## Network APIs

The CefSharp host supplies default `GM.xmlHttpRequest`, `GM.cookie`, and `GM.webRequest` services. Set `EnableDefaultNetworkServices = false` to require explicit providers, or use `UseHttpRequestService`, `UseCookieService`, and `UseWebRequestService` to replace individual defaults.

### HTTP Requests

Declare `@grant GM.xmlHttpRequest` or `@grant GM_xmlhttpRequest` and an `@connect` entry for every destination. Methods are case-insensitive valid HTTP tokens except `CONNECT`, `TRACE`, and `TRACK`.

Request bodies support strings, `ArrayBuffer`, typed arrays, `DataView`, `Blob`/`File`, `FormData`, `URLSearchParams`, plain objects, and arrays. Objects and arrays use JSON; `URLSearchParams` uses form encoding; `FormData` retains its generated boundary, filename, and media type. Bodies cross the bridge in 64 KiB chunks.

Supported controls include `headers`, `cookie`, `user`, `password`, `anonymous`, `overrideMimeType`, `signal`, `redirect`, `nocache`, `revalidate`, `fetch`, `timeout`, and `context`. Redirect mode defaults to `follow` and also accepts `error` and `manual`. Every redirect is rechecked against the grant, `@connect`, and host permission policy. Explicit cookies and authorization are removed on cross-origin redirects.

Compatibility headers such as `User-Agent`, `Referer`, `Origin`, and `Cookie` may be set. MonkeySharp rejects `Host`, `Content-Length`, connection and transfer-control headers, and all `Sec-*` and `Proxy-*` headers. `anonymous:true` disables stored cookies and credentials. `fetch:true` ignores timeout and progress and reports only readyState 4.

`responseType` accepts `text`, `json`, `arraybuffer`, `blob`, and `stream`. Non-stream responses are assembled from 64 KiB host reads. The response exposes readyState 1 through 4, status, final URL, raw repeated response headers, response data, applicable response text, and the original JavaScript context. HTTP 4xx and 5xx responses call `onload`; network, permission, serialization, and bridge failures call `onerror`.

Callbacks are `onloadstart`, `onreadystatechange`, `onprogress`, `onuploadprogress`, `onload`, `onerror`, `ontimeout`, `onabort`, and `onloadend`. `GM.xmlHttpRequest` returns a Promise with `.abort()`; `GM_xmlhttpRequest` immediately returns an abort handle and reports through callbacks. `AbortSignal` and stream cancellation propagate to the host.

Request and response sizes are unlimited by default. Each direction stays in memory through 256 KiB and then spills to a temporary file, which is removed on every terminal path. Body transfer and the long-running `execute` operation are independent of the bridge's general message and request limits.

`proxy`, `cookiePartition`, and `Blob`/`File` URLs return `MSP006_NOT_SUPPORTED`. Partitioned-cookie keys are browser-extension manager state that the public CefSharp request-context API does not expose.

### Cookies

Declare `@grant GM.cookie` or `@grant GM_cookie`. The default provider uses the attached browser request context. An omitted `details.url` uses the current document URL; an explicit URL must be covered by the script's `@match` or `@include` rules. Core validates domain, path, secure, SameSite, `expirationDate`, `httpOnly`, and first-party fields.

```javascript
const cookies = await GM.cookie.list({ url: location.href });
await GM.cookie.set({ url: location.href, name: "session", value: "ready", path: "/" });
await GM.cookie.delete({ url: location.href, name: "session" });
const listenerId = GM.cookie.addListener({}, change => console.log(change.cause));
```

`GM_cookie.list` calls back as `(cookies, error)`, while `set` and `delete` call back as `(error)`. Successful operations pass `null`/`undefined` for the error value. `addListener/removeListener` use numeric IDs. Listener registrations are released with the execution, document, frame, or host.

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

`GM_webRequest(rules, listener)` is the legacy alias of the same registration function. Listener calls use `(info, message, details)` and report only `cancel` or `redirect` results. The adapter rejects an occupied non-multiplexer request handler rather than replacing it.

## Host Service Contracts

The builder is single-use. The application owns the repository, browser, and every object supplied through `UseValueStore`, `UsePermissionPolicy`, or a `Use...Service`/`Use...Provider` method. The host owns its engine, gateway, internally created providers, default network services, and default value store. Objects passed to `AddApiProvider` transfer to the gateway and are disposed when applicable.

Provider requirements:

- `IUserScriptValueStore` operations for one `ScriptKey` must be linearizable. Cancellation before commit makes no change; cancellation after commit completes successfully and emits one notification.
- `IUserScriptDependencyProvider` owns trusted download, caching, HTTPS policy, and optional integrity validation for `@require`. `ResourceScriptSourceResolver` preserves declaration order and enforces the byte limit.
- In `LegacyCompatible`, a missing dependency provider skips an invocation that declares `@require` with `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE`. A provider failure emits `MSR400_DEPENDENCY_RESOLUTION_FAILED`. In `ModernStrict`, a missing provider leaves the installed main source unchanged.
- `IResourceProvider` returns previously authorized content for a declared resource. Resource data is limited by `BridgeOptions.MaxResourceBytes`.
- `IHttpRequestService` receives a repeatable `IUserScriptHttpBody` and observer before work begins. It reports metadata before body data, honors `MaxResponseBytes`, calls `RedirectAllowed` before following redirects, and records followed URLs for Core to recheck against `@connect`.
- `IUserScriptPermissionPolicy` receives the installation, frame, method, target summary, and current capabilities for every host-backed request.
- `IUserScriptWindowService` backs the exact `window.close` and `window.focus` grants. `CloseAsync` may refuse to close the last tab; omit `UseWindowService` to leave both properties unavailable. `window.onurlchange` is core-only and reports same-document `pushState`, `replaceState`, `popstate`, and `hashchange` changes.

## Lifecycle and Diagnostics

The adapter creates an identity per frame document and executes each `(document, frame, script, run-at)` once. Navigation, context release, detach, and disposal invalidate capabilities and cancel pending work.

Grant state is significant: a missing `@grant` declaration exposes no `GM` facade, while explicit `@grant none` exposes only `GM.info`/`GM_info`. `GM.info` is a deep-frozen snapshot of normalized metadata, grants, URL rules, resources, update state, handler, sandbox mode, and raw metadata text.

CefSharp exposes only best-effort document-start hooks. Selected document-start executions emit `MSR100_DOCUMENT_START_BEST_EFFORT`. Set `RequireGuaranteedDocumentStart = true` to skip them with `MSR101_DOCUMENT_START_UNAVAILABLE`.

`@sandbox` values other than `raw` emit `MSR212_UNSUPPORTED_SANDBOX`, and `@unwrap` emits `MSR213_UNSUPPORTED_UNWRAP`. Both declarations are preserved in metadata and `GM.info`, but execution remains in the page world and inside the MonkeySharp wrapper.

Protocol errors use stable codes `MSP001` through `MSP010` and `MSP999`. Diagnostics never include capability tokens or stored values. Default bridge limits are 1 MiB per request, 1 MiB per response, 10 MiB per resource, 30 seconds per ordinary request, and 64 pending requests per document; configure them through `CefSharpHostOptions.Bridge`.

## Migration Notes

Version 3 host service contracts are source-breaking. `IHttpRequestService.SendAsync` receives an `IUserScriptHttpObserver` before starting and returns `IHttpRequestOperation`; request bodies are repeatable streams, response data is incremental, and redirect authorization is asynchronous. Existing adapters should wrap old tasks in operation objects and forward response start, data, progress, completion, and abort.

Notification, tab, and download services similarly return lifecycle handles or operations with completion and cancellation members. `ITabStateService` accepts JSON objects only. `UseCookieService` and `UseWebRequestService` are optional and do not affect hosts that omit those providers.

The former one-shot `GM_xmlhttpRequest` payload/result is now a `create`, `appendBody`, `execute`, `readBody`, `abort`, and `release` lifecycle session. The former `ForceUseStrict`/`with(window)` execution is replaced by a strict `Function` scope with explicit `GM`, `unsafeWindow`, and `window` parameters.

Compatibility is built into the default profile; there is no separate transport or WCF package. Migrate storage keys explicitly if v1 data must be retained. Applications choosing `ModernStrict` should update metadata grants to canonical `GM.*` names.

Browser-extension-only cookie partitioning, manager UI state, download shelf presentation, notification persistence, and request interception APIs that require waiting on JavaScript remain host-specific and outside the compatibility promise. A host should expose only the documented provider contract and return `MSP006_NOT_SUPPORTED` when a capability is absent.

## Build, Package, and Verify

Use the same `CefSharpVersion` property for restore, build, test, and package commands:

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=121.3.70
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`. The x86 solution configuration excludes the x64-only Demo projects. Package Core and each adapter platform into separate directories:

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=121.3.70 -o artifacts/packages/x86
```

Run the real-Chromium gates from a Windows desktop session:

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 121.3.70
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 121.3.70
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 121.3.70
```

Pass the same `-CefSharpVersion` to every restore/build/test/pack/SmokeHost command when validating another supported version. Run the SmokeHost commands for x64 and x86 at `84.4.10`, `121.3.70`, and `151.3.240`; the Demo gate stays on x64/default `121.3.70`. The PowerShell runner passes that version to its restore, clean, build, and run steps. Both gates use isolated profiles and `TrustedPageWorld`; they are integration checks, not security sandboxes.
