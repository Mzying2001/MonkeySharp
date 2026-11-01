# MonkeySharp Userscript Guide

**English** | [简体中文](usage-guide.zh-CN.md)

This guide describes the JavaScript userscript surface in the current MonkeySharp 3 source tree. It covers writing scripts, every built-in script API, and differences from Tampermonkey. Applications can change services, permissions, limits, and compatibility profiles. For .NET integration, see the [integration reference](integration-reference.md); for installation and UI operations, see the [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md).

Tampermonkey comparisons were checked against its [official documentation](https://www.tampermonkey.net/documentation.php) on **2026-11-01**. They describe documented contracts, not a differential test against every Tampermonkey/browser version.

## Contents

- [Write and install a script](#write-and-install-a-script)
- [Metadata](#metadata)
- [Execution, grants, and compatibility](#execution-grants-and-compatibility)
- [API index](#api-index)
- [Information and logging](#information-and-logging)
- [Values and change listeners](#values-and-change-listeners)
- [DOM helpers](#dom-helpers)
- [Resources](#resources)
- [HTTP requests](#http-requests)
- [Menus, notifications, and clipboard](#menus-notifications-and-clipboard)
- [Tabs and tab state](#tabs-and-tab-state)
- [Downloads](#downloads)
- [Cookies](#cookies)
- [Request interception](#request-interception)
- [Window extensions](#window-extensions)
- [Tampermonkey compatibility checklist](#tampermonkey-compatibility-checklist)
- [Errors and troubleshooting](#errors-and-troubleshooting)
- [Reference sources](#reference-sources)

## Write and install a script

### First script: change the page

Save the following complete UTF-8 file as `page-marker.user.js`:

```javascript
// ==UserScript==
// @name         Page marker
// @namespace    https://example.com/my-scripts
// @version      1.0.0
// @description  Show that a userscript ran
// @match        https://example.com/*
// @run-at       document-end
// @grant        none
// ==/UserScript==

(function () {
    const marker = document.createElement("p");
    marker.textContent = "MonkeySharp is running.";
    document.body.prepend(marker);
})();
```

In the WPF Demo, open the script manager, create a script or import this file, review the source, and choose **安装 / 保存** (install/save). Confirm installation, enable the script, and visit or reload `https://example.com/`. The marker should appear. Installing or editing a script refreshes all open Demo tabs. Other applications choose their own installation UI; opening a `.user.js` URL does not by itself guarantee installation.

`@match` selects pages, `@run-at` selects the lifecycle stage, and `@grant none` is sufficient for ordinary page DOM operations. It does not grant storage, network, or host interaction APIs.

### Second script: persist a counter

This complete script needs a host that permits privileged grants. The Demo enables the required `TrustedPageWorld` option explicitly.

```javascript
// ==UserScript==
// @name         Visit counter
// @namespace    https://example.com/my-scripts
// @version      1.0.0
// @match        https://example.com/*
// @noframes
// @run-at       document-end
// @grant        GM.getValue
// @grant        GM.setValue
// @grant        GM.addStyle
// ==/UserScript==

(async function () {
    const count = await GM.getValue("visits", 0) + 1;
    await GM.setValue("visits", count);
    await GM.addStyle("#ms-counter { padding: 8px; background: #fff3c4; }");
    const badge = document.createElement("p");
    badge.id = "ms-counter";
    badge.textContent = `Visits: ${count}`;
    document.body.prepend(badge);
})().catch(error => console.error("Visit counter failed", error));
```

Reload to increment the count. Values belong to the installation, not the website. Durability across application restarts depends on the host's value store; the Demo uses SQLite. A read followed by a write is not an atomic increment across tabs.

Use an async function and catch its rejection. Top-level `await` is unavailable because source runs in a normal function wrapper, not an ES module. Later examples are fragments: add their listed `@grant` declarations to your script's metadata and put `await` expressions inside an async function.

Use F12 in the Demo to open DevTools. `GM` and `GM_*` are wrapper-local bindings, so they need not exist in the page console or as `window.GM`. Log from the script or set a breakpoint in its execution.

## Metadata

The header begins with `// ==UserScript==` and ends with `// ==/UserScript==`. Put it first for portability. The default parser allows a preamble within 128 lines/64 Ki UTF-16 characters; `ModernStrict` requires the first non-empty line to be the header. Metadata errors prevent installation; warnings remain available to the host. For known single-valued keys, the first occurrence wins and duplicates produce a warning; repeat collection fields on separate lines. Unknown entries are preserved but do not acquire runtime behavior.

| Fields | Syntax and behavior |
| --- | --- |
| `@name`, `@namespace`, `@version` | Script identity and version. Use stable, non-empty name and namespace, especially for updates. Installation identity/storage is a separate stable `ScriptKey`. |
| `@description`, `@author`, `@license`, `@copyright` | Descriptive strings. `@name:locale` and `@description:locale` preserve localized variants. |
| `@icon`, `@iconURL`, `@icon64`, `@icon64URL` | Icon URLs. URL-suffixed forms take precedence over their respective short forms; the Demo prefers the 64-pixel icon. |
| `@match pattern` | Repeatable positive WebExtension-style URL patterns. At least one matching `@match` or `@include` is needed for execution. |
| `@include pattern` | Repeatable URL globs or slash-delimited .NET regular expressions, for example `/^https:\/\/example\.com\//`. Regex evaluation has a 100 ms limit. |
| `@exclude`, `@exclude-match` | Negative glob/regex rules and negative match patterns respectively. Any exclusion overrides positive matches. Invalid match patterns and malformed regexes are errors. |
| `@run-at value` | `document-start`, `document-body`, `document-end`, `document-idle` (default), or `context-menu`. See execution rules below. |
| `@noframes` | Presence suppresses child-frame execution; no value is needed. Otherwise every matching frame may execute. |
| `@grant name` | Repeat for each exact capability other than the always-available info properties. Canonical and recognized legacy spellings are case-sensitive. `none` cannot be combined with another grant. Unknown grants warn and are not exposed. |
| `@connect target` | XHR destination allowlist: host, IP, `localhost`, `*.example.com`, `self`, `*`, or absolute HTTP(S) URL. See [HTTP requests](#http-requests). |
| `@require url` | Dependency source executed before the main source in declaration order, through a host dependency provider. Not an API grant. |
| `@resource name url` | Named resource, read through the resource APIs and a host resource provider. Resource names are case-sensitive. |
| `@homepage`, `@homepageURL`, `@website`, `@source`, `@supportURL` | Source/support links. Homepage priority is `homepageURL`, `homepage`, `website`, then `source`. |
| `@downloadURL`, `@updateURL` | Update information. The Demo offers manual check/install; the runtime does not schedule automatic updates. `@downloadURL none` disables checking. |
| `@antifeature type description` | Disclosure information, including localized `@antifeature:locale` declarations. |
| `@webRequest JSON` | Validated selector/action metadata, exposed in `GM.info.script.webRequest`. It does **not** activate interception. Register with `GM.webRequest`. |
| `@run-in`, `@inject-into`, `@sandbox`, `@unwrap` | Preserved execution declarations. They do not create extension worlds, private/container selection, or remove the wrapper. Non-raw sandbox modes and unwrap produce diagnostics. |

### Matching and lifecycle

`@match https://example.com/*` covers that host; `@match https://*.example.com/*` covers subdomains **without** the bare `example.com`. List both when needed. `*://` and `http*://` cover HTTP/HTTPS; explicit ports are checked. URL hosts are IDN-normalized, fragments are ignored, and query strings participate in matching. This subdomain wildcard behavior differs from the conventional WebExtension/Tampermonkey match contract; see the [official match description](https://www.tampermonkey.net/documentation.php?q=include).

MonkeySharp also accepts `file:///*`, `about:blank`, and `<all_urls>` (HTTP, HTTPS, and file, not about:blank), subject to pages the application actually exposes. Tampermonkey's documented match syntax does not support `<all_urls>`. Include/exclude globs match the whole normalized URL case-sensitively, with `*` and `?`; they do not implement Tampermonkey's host-aware include heuristics. Prefer `@match` when selecting a host. Metadata regexes use .NET syntax and `/expression/` delimiters, without JavaScript literal flags such as `/i`.

| Stage | MonkeySharp behavior |
| --- | --- |
| `document-start` | Best effort through CefSharp; not guaranteed before page scripts. A host requiring guaranteed start can skip these executions. |
| `document-body` | Runs when the body becomes available. |
| `document-end` | Associated with DOMContentLoaded. |
| `document-idle` | Associated with the load lifecycle event. Do not assume Tampermonkey's scheduling heuristics. |
| `context-menu` | Runs only when the user selects the matching script's native context-menu entry; each selection creates a fresh execution. |

Each `(document, frame, script, run-at)` executes once. Same-document navigation updates matching context and can emit `urlchange`; it does not automatically rerun the same lifecycle invocation. Use [window.onurlchange](#window-extensions) or DOM observers for SPA updates. Full navigation revokes the old execution and its pending operations, listeners, and registrations.

### Dependencies, resources, and updates

`@require` and `@resource` URLs accept `md5`/`sha256` integrity fragments: `#sha256=<digest>` or `#sha256-<digest>`, with hexadecimal or standard Base64 digests. Comma/semicolon-separated declarations select the last supported hash. Fragments are stripped before fetching/caching. Original bytes are checked before use and on cache reads. Malformed declarations, unusable unsupported hashes, and mismatches prevent loading.

The host controls fetching and caching. In `LegacyCompatible`, a missing dependency provider skips scripts declaring `@require`; in `ModernStrict`, the main source is left unchanged when that provider is missing, so dependencies are not supplied. Do not rely on undeclared globals or assume CDN libraries are built in. Resource APIs require their own grants and a resource provider.

The Demo's manual updater checks `@updateURL` before `@downloadURL`, and installs from `@downloadURL` before the check source. Candidates require the same non-empty name/namespace and a newer downloaded version. Applying an update preserves installation identity, enabled state, and stored values. Its downloader has no browser login cookies. See the [update and integrity reference](integration-reference.md#resource-integrity-and-update-checks).

## Execution, grants, and compatibility

MonkeySharp executes trusted source in the **page world**, inside a function wrapper. Its CefSharp bridge reports unverified integrity. The default host suppresses scripts with explicit privileged grants unless the application opts into `TrustedPageWorld`. The Demo makes this opt-in. Grants constrain exposed capabilities but are not protection against hostile page-world JavaScript.

| Declaration | Exposed surface |
| --- | --- |
| No `@grant` | `GM.info` and `GM_info`; ordinary page operations remain possible. |
| Only `@grant none` | `GM.info` and `GM_info`. |
| Explicit grant list | `GM.info` and `GM_info`, plus individually granted capabilities with an available implementation. |

Declaring a legacy alias normally authorizes its canonical capability and both facades may be available. For example, `@grant GM_getValue` normalizes to `GM.getValue`. This does not grant `GM.setValue`. Hosts can disable alias parsing separately.

| Profile | Behavior |
| --- | --- |
| `LegacyCompatible` (default) | Non-strict wrapper; top-level `this` is the window facade; modern APIs and enabled `GM_*` aliases. Storage/resource aliases depend on complete bootstrap snapshots. |
| `ModernStrict` | Strict wrapper; top-level `this` is `undefined`; only `GM_info` remains available among legacy globals. Modern APIs retain their individual return contracts, including synchronous IDs/handles. |

The host can independently disable legacy globals other than `GM_info`, or snapshots. Bootstrap snapshots default to a 1 MiB budget and a five-second preparation timeout; failed, incomplete, or oversized snapshots leave synchronous storage/resource aliases unavailable. Modern methods can still use the bridge. A store without atomic snapshot support emits a degradation diagnostic. Prefer modern APIs when you need observable completion/failure, while recognizing that reads in the default profile can also use the local mirror.

APIs needing menus, notifications, clipboard, tabs, downloads, tab state, or window control are absent without their corresponding host service. CefSharp supplies XHR, Cookie, and webRequest services by default unless disabled/replaced. Check capabilities with `typeof GM !== "undefined" && typeof GM.download === "function"`; adding a grant cannot install a missing service. See the [host service contracts](integration-reference.md#host-service-contracts).

## API index

Each row requires the capability in its first column unless stated otherwise. `Promise<T>` means use `await`; an immediate ID/handle is not a readiness guarantee for its background registration. Legacy names require the legacy profile. Details, errors, and examples follow.

| Capability / modern member | Legacy name | Modern result | Implementation |
| --- | --- | --- | --- |
| `GM.info` | `GM_info` | Frozen object, not a function | Core |
| `GM.log` | `GM_log` | `Promise<undefined>` | Core logging sink |
| `GM.getValue` | `GM_getValue` | `Promise<JSON value or undefined>` | Value store |
| `GM.setValue` | `GM_setValue` | `Promise<undefined>` | Value store |
| `GM.deleteValue` | `GM_deleteValue` | `Promise<boolean>` | Value store |
| `GM.getValues` | `GM_getValues` | `Promise<object>` | Value store |
| `GM.setValues` | `GM_setValues` | `Promise<undefined>` | Value store |
| `GM.deleteValues` | `GM_deleteValues` | `Promise<undefined>` | Value store |
| `GM.listValues` | `GM_listValues` | `Promise<string[]>` | Value store |
| `GM.addValueChangeListener` | `GM_addValueChangeListener` | Number immediately | Core |
| `GM.removeValueChangeListener` | `GM_removeValueChangeListener` | `Promise<boolean>` | Core |
| `GM.addStyle` | `GM_addStyle` | `Promise<HTMLStyleElement>` | Page DOM |
| `GM.addElement` | `GM_addElement` | `Promise<Element>` | Page DOM |
| `GM.getResourceText` | `GM_getResourceText` | `Promise<string>` | Resource provider |
| `GM.getResourceURL` | `GM_getResourceURL` | `Promise<string>` | Resource provider |
| `GM.xmlHttpRequest` | `GM_xmlhttpRequest` | Abortable `Promise<Response>` | CefSharp default |
| `GM.registerMenuCommand` | `GM_registerMenuCommand` | `Promise<number>` | Menu service |
| `GM.unregisterMenuCommand` | `GM_unregisterMenuCommand` | `Promise<boolean>` | Menu service |
| `GM.notification` | `GM_notification` | `Promise<{id: string}>` | Notification service |
| `GM.setClipboard` | `GM_setClipboard` | `Promise<undefined>` | Clipboard service |
| `GM.openInTab` | `GM_openInTab` | Tab handle immediately | Tab service |
| `GM.getTab` | `GM_getTab` | `Promise<object>` | Tab-state service |
| `GM.saveTab` | `GM_saveTab` | `Promise<undefined>` | Tab-state service |
| `GM.getTabs` | `GM_getTabs` | `Promise<object>` | Tab-state service |
| `GM.download` | `GM_download` | `Promise<DownloadHandle>` | Download service |
| `GM.cookie` | `GM_cookie` | Object with CRUD/listener methods | CefSharp default |
| `GM.webRequest` | `GM_webRequest` | Removable handle immediately | CefSharp default |
| `unsafeWindow` | Same name | Page global object | Exact grant |
| `window.close`, `window.focus` | Same names | `Promise<boolean>` each | Window service |
| `window.onurlchange` | Same name | Property/event registration | Core |

## Information and logging

### GM.info / GM_info

Read-only, recursively frozen installation snapshot. `GM_info === GM.info`. Both names are always available without a grant, including in `ModernStrict` and when legacy globals are disabled. Scripts with no grants, `@grant none`, or only info grants need no bridge binding or handshake. Explicit info grants remain supported; metadata retains the original declarations. There are no parameters and no Promise. Missing optional metadata is generally `null`.

| Fields | Meaning |
| --- | --- |
| `scriptKey` | Stable installation identifier. |
| `name`, `namespace`, `version`, `description` | Top-level metadata conveniences. |
| `grants`, `grantDeclarationState` | Original declared names and `Missing`, `ExplicitNone`, or `ExplicitList`. Grant aliases are normalized internally; these lists retain declaration spelling. |
| `scriptHandler`, `sandboxMode` | Fixed `"MonkeySharp"` and `"raw"`. |
| `downloadMode`, `isIncognito`, `scriptWillUpdate` | Fixed `"disabled"`, `false`, `false`; these are compatibility placeholders, **not capability detection**. A download service can still exist. |
| `scriptMetaStr`, `scriptUpdateURL` | Raw metadata header and declared update URL. |
| `compatibility` | `contractVersion: 1`, `handler: "MonkeySharp"`, `executionWorld: "page"`, `isolatedWorld: false`; `fields` records field provenance. |
| `script` | Metadata object detailed below. |

`script` contains `name`, `namespace`, `version`, `description`, `author`, `copyright`, `name_i18n`, `description_i18n`, `icon`, `icon64`, `homepage`, `supportURL`, `downloadURL`, `updateURL`, `fileURL` (installation source), `header`, `grant`, `connects`, `matches`, `includes`, `excludes` (including exclude-match entries), `resources`, `antifeatures`, `webRequest`, `run-at`, and `unwrap`. `lastModified` is installation-update Unix **milliseconds**; `position` is fixed `0`, and `blockers` is an empty array. `resources` entries are `{name, url, integrity: [{algorithm, digest}]}`; antifeatures are grouped by type, then locale (`default` for unlocalized). `webRequest` is `null` when absent. `options` contains `sandbox`, `noframes`, `unwrap`, and `run_at`.

**Tampermonkey difference:** this is MonkeySharp's snapshot, with explicit provenance and placeholders; it does not promise every [Tampermonkey GM_info field](https://www.tampermonkey.net/documentation.php?q=GM_info), live browser settings, or extension update state.

### GM.log(value) / GM_log(value)

`value` must be JSON-serializable under the rules in the next section. Modern calls return `Promise<undefined>` after delivery to the host logging callback. If no sink is configured, delivery has no visible output. `GM_log` returns `undefined`, additionally writes to the page console, and reports asynchronous host failures to the console. Only one value is accepted; this is not console.log's variadic formatting API.

```javascript
// @grant GM.log
(async function () {
    await GM.log({ script: GM.info.name, version: GM.info.version });
})().catch(console.error);
```

## Values and change listeners

Values are isolated by installation `ScriptKey`, shared by its active tabs/frames, and independent of page localStorage. Reinstalling as a different installation need not reuse values. The Demo preserves values when updating an existing installation and removes them on uninstall; other hosts choose persistence policy.

Keys are non-empty strings. Values/defaults are cloned through JSON serialization: use strings, finite numbers, booleans, `null`, arrays, and plain JSON objects. Top-level `undefined`, functions, symbols, BigInt, and cycles are invalid. Nested unsupported properties, dates, and non-finite numbers follow `JSON.stringify` conversion, so avoid them when type preservation matters. Returned objects are copies; edit and save them explicitly.

| Signature | Parameters and behavior | Result |
| --- | --- | --- |
| `GM.getValue(key, defaultValue?)` | Return the stored value if `key` exists; otherwise return the provided JSON default, or `undefined` when no default is supplied. Passing `undefined` is equivalent to omitting the default. | `Promise<value>` |
| `GM.setValue(key, value)` | Replace the stored value; serializes before committing. | `Promise<undefined>` |
| `GM.deleteValue(key)` | Delete if present. | `Promise<boolean>` indicating whether it existed; with a mirror, this is the local pre-mutation existence state. |
| `GM.getValues(keysOrDefaults?)` | Read a string array of keys (missing keys are omitted), or an object of defaults. Omitted, `null`, or `undefined` reads all values; `[]` and `{}` return `{}`. An `undefined` default means no default. | `Promise<object>` |
| `GM.setValues(values)` | Set the object's own enumerable string keys; validate and JSON-clone every value before changing storage. | `Promise<undefined>` |
| `GM.deleteValues(keys)` | Delete an array of keys, ignoring duplicates and absent keys. | `Promise<undefined>` |
| `GM.listValues()` | List keys; mirrored results are sorted. | `Promise<string[]>` |
| `GM.addValueChangeListener(key, callback)` | Register `callback(key, oldValue, newValue, remote)`; missing/deleted values are `undefined`. | Positive number immediately; registration failure is logged and removes the local handler. |
| `GM.removeValueChangeListener(listenerId)` | Positive integer; remove this execution's listener. | `Promise<boolean>` indicating removal. |

Grant each method separately; a bulk grant does not grant its single-key counterpart, or vice versa. Listeners cover active executions of the same installation, not unrelated scripts. Remote changes pass `remote: true`. With a storage mirror, successful local writes notify with `false` after commit. Bulk writes notify once per committed key, including without a mirror and for keys committed before a later failure; deleting an absent key does not notify. For single-key writes, same-execution host echoes are suppressed: without a mirror (including `ModernStrict`), do not rely on notifications for your own single-key writes; update your own UI after awaiting the write. Listeners end with the execution.

Legacy signatures use the same parameters: `GM_getValue` returns a value immediately, `GM_getValues` returns an object immediately, `GM_listValues` returns a sorted array, `GM_setValue`/`GM_deleteValue`/`GM_setValues`/`GM_deleteValues` return `undefined`, `GM_addValueChangeListener` returns a numeric ID, and `GM_removeValueChangeListener` returns a local removal boolean. The synchronous reads/writes and legacy removal method require a complete storage mirror. Writes change it immediately and queue serialized asynchronous commits. Failed commits roll back/reconcile and emit `MSC413_COMPATIBILITY_MUTATION_FAILED`; a successful synchronous return does not mean durable persistence. Modern writes in the default mirrored profile use this queue too, but their Promises expose completion/failure.

Each bulk write/delete uses one bridge request and commits keys sequentially through the existing value store. It is not an atomic transaction: a storage failure stops the batch and preserves previously committed keys. Validation failures change no keys. The mirror is reconciled after a failed commit, with later queued mutations reapplied. Empty batches are valid. Bulk values follow the same JSON rules as single-key values; stored `undefined` is not supported.

```javascript
// @grant GM.getValues
// @grant GM.setValues
// @grant GM.deleteValues
(async function () {
    await GM.setValues({ theme: 'dark', enabled: true });
    const settings = await GM.getValues({ theme: 'light', enabled: false });
    console.log(settings);
    await GM.deleteValues(['theme', 'enabled']);
})().catch(console.error);
```

**Tampermonkey differences:** JSON cloning does not preserve stored `undefined`; modern `GM.addValueChangeListener` returns an immediate number rather than the Promise described in [Tampermonkey's values reference](https://www.tampermonkey.net/documentation.php?q=GM_values). Mirror limits and local-listener behavior above are MonkeySharp-specific.

```javascript
// @grant GM.getValue
// @grant GM.setValue
// @grant GM.deleteValue
// @grant GM.listValues
// @grant GM.addValueChangeListener
// @grant GM.removeValueChangeListener
(async function () {
    const id = GM.addValueChangeListener("settings", (key, oldValue, newValue, remote) => {
        console.log(key, oldValue, newValue, remote);
    });
    const settings = await GM.getValue("settings", { enabled: true });
    settings.enabled = !settings.enabled;
    await GM.setValue("settings", settings);
    console.log(await GM.listValues());
    await GM.removeValueChangeListener(id);
    console.log("Deleted:", await GM.deleteValue("settings"));
})().catch(console.error);
```

## DOM helpers

### GM.addStyle(css) / GM_addStyle(css)

Requires the corresponding grant. `css` is a string. Creates a `<style>`, assigns `textContent`, and appends it to `document.head` or `document.documentElement`. Modern result: `Promise<HTMLStyleElement>`; legacy result: that element immediately. Remove it with `element.remove()`. Invalid parameters cause a rejection/throw respectively; DOM errors propagate.

### GM.addElement(tagName, attributes?) / GM.addElement(parent, tagName, attributes?)

Legacy: `GM_addElement` with the same overloads; grant `GM.addElement` or its alias. `tagName` is a non-empty string. `parent` must have `appendChild`; if omitted, the parent is `document.body` or `document.documentElement`.

`attributes` is an optional object. `textContent`/`innerHTML` set content, string `style` sets `style.cssText`, existing DOM properties (including function event handlers) are assigned, and other fields use `setAttribute` with string conversion. Modern result: `Promise<Element>`; legacy: immediate `Element`. Invalid inputs and DOM failures reject/throw rather than returning `null`.

**Tampermonkey difference:** these are ordinary page DOM operations, with no extension-based CSP bypass. The omitted addElement parent prefers the body; do not assume the [Tampermonkey helper's placement/error behavior](https://www.tampermonkey.net/documentation.php?q=GM_addElement).

```javascript
// @grant GM.addStyle
// @grant GM.addElement
(async function () {
    const style = await GM.addStyle(".ms-button { color: darkgreen; }");
    const button = await GM.addElement(document.body, "button", {
        className: "ms-button", textContent: "Remove style", onclick: () => style.remove()
    });
    console.log(button);
})().catch(console.error);
```

## Resources

### GM.getResourceText(name) / GM_getResourceText(name)

Requires its grant, a resource provider, and `@resource name url`. `name` is a non-empty, case-sensitive string. Modern result: `Promise<string>`, using provider text or UTF-8 decoding of bytes. Legacy returns a string immediately from the complete bootstrap resource snapshot. An undeclared resource rejects; a missing legacy snapshot makes the alias unavailable, and an absent name in an available snapshot throws.

### GM.getResourceURL(name) / GM_getResourceURL(name)

Same prerequisites and name rules, with its own grant. Modern result: `Promise<string>` containing a **data URL**, `data:<media-type>;base64,<bytes>`; legacy returns that URL immediately from its snapshot. It is not the download URL and is not a revocable object URL. There is no supported `isBlobUrl` parameter or alternate-casing alias. Integrity and size limits apply to both resource APIs.

**Tampermonkey difference:** Tampermonkey's modern name is `GM.getResourceUrl`, while MonkeySharp only exposes `GM.getResourceURL`; both use the legacy name `GM_getResourceURL`. Use MonkeySharp's exact spelling and data-URL contract; do not assume browser-extension/object-URL behavior or additional options from other managers. See the [official resource reference](https://www.tampermonkey.net/documentation.php?q=GM_getResource).

Replace the badge URL in this fragment with an actual reachable image resource; the example domain does not guarantee a favicon at that path.

```javascript
// @resource badge https://example.com/favicon.ico
// @grant GM.getResourceURL
(async function () {
    const image = document.createElement("img");
    image.src = await GM.getResourceURL("badge");
    document.body.appendChild(image);
})().catch(console.error);
```

For a text resource, declare its URL in `@resource config ...`, grant `GM.getResourceText`, and read `const text = await GM.getResourceText("config")` inside your async entry point. Parsing JSON is the script's responsibility.

## HTTP requests

### GM.xmlHttpRequest(details) / GM_xmlhttpRequest(details)

Grant exactly `GM.xmlHttpRequest` (capital H) or `GM_xmlhttpRequest`, plus an `@connect` declaration for each destination. Needs an HTTP service; CefSharp's default follows the browser request context, including applicable cookies/credentials unless anonymous. This host request path is separate from page fetch and its CORS restrictions; grants and host policy still apply.

Modern result: a `Promise<Response>` with an immediate `.abort()` method. Non-stream responses resolve after body assembly; stream responses resolve when response metadata/stream is available and completion/errors can occur later. Legacy result: an immediate `{abort()}` handle; responses arrive through callbacks. Aborting has no useful return value. Callback exceptions are logged. HTTP 4xx/5xx **resolve and call onload**; inspect `status` yourself.

#### Request parameters

| `details` field | Type / default | Behavior |
| --- | --- | --- |
| `url` | Required absolute HTTP(S) string or `URL` | Relative URLs and Blob/File URLs are unsupported. |
| `method` | String, `"GET"` | Valid HTTP token, case-insensitive; CONNECT, TRACE, TRACK are rejected. |
| `headers` | String-to-string object, `{}` | Header names/values validated; CR/LF injection rejected. |
| `data` | Optional body | String, ArrayBuffer, typed array, DataView, Blob/File, FormData, URLSearchParams, object, or array. Null/omission gives no body. |
| `binary` | Boolean, `false` | Requires string body; sends low eight bits of each UTF-16 code unit. Normal strings use UTF-8. |
| `responseType` | String, `"text"` | `text`, `json`, `arraybuffer`, `blob`, `document`, `xml`, or `stream`; empty string means text. |
| `responseXML` | Boolean, `false` | `true` overrides responseType to `document`, parsed through the page DOMParser as XML. |
| `timeout` | Non-negative integer milliseconds | Zero/omission supplies no per-XHR deadline. Ignored with `fetch:true`. Host policy may impose limits. |
| `cookie` | Optional string | Explicit cookie header input. |
| `user`, `password` | Optional strings | Authentication credentials supplied to the host HTTP service. |
| `anonymous` | Boolean, `false` | Disables browser-stored cookies and credentials. |
| `overrideMimeType` | Optional string | Override response MIME/charset handling. |
| `redirect` | `"follow"` (default), `"error"`, `"manual"` | Follow redirects, fail, or return without following. Every followed target is reauthorized. |
| `nocache`, `revalidate` | Booleans, `false` | Cache-control requests passed to the HTTP service. |
| `fetch` | Boolean, `false` | Compatibility mode: ignore timeout/progress and expose only readyState 4. Does not switch this API to page fetch. |
| `context` | Any JavaScript value | Returned locally as the same `response.context`; not serialized through the host. |
| `signal` | Optional AbortSignal | Cancels the operation; stream cancellation also aborts it. |
| Callback fields | Optional functions | See the callback table. `upload.onprogress` is also accepted and takes precedence over `onuploadprogress`. |
| `proxy`, `cookiePartition` | Unsupported | Supplying either returns/throws `MSP006_NOT_SUPPORTED`. |
| `synchronous:true`, `async:false` | Unsupported | Rejected with `MSP006_NOT_SUPPORTED`; the API is asynchronous. |

Objects/arrays are sent as JSON, URLSearchParams as form encoding, and FormData as multipart with generated boundary/filenames/media types. Appropriate content type is supplied unless you set it yourself. Blob/File **bodies** are supported even though Blob/File **URLs** are not.

`User-Agent`, `Referer`, `Origin`, and `Cookie` headers are allowed. `Host`, `Content-Length`, connection/transfer-control headers, and `Sec-*`/`Proxy-*` names are rejected. Cross-origin redirects strip explicit cookies and authorization. Cookie merging and browser-authentication behavior remain host-service details; do not assume exact Tampermonkey browser-version behavior.

`@connect self` checks scheme, host, and port. A bare domain allows itself and its subdomains; `*.example.com` allows only subdomains. IPs and localhost are exact. URL declarations additionally constrain scheme/port; their path is not an allowlist restriction. An omitted allowlist denies even same-origin XHR. Original and redirected URLs must all pass the allowlist and host permission policy.

#### Response and callbacks

| Response field | Meaning |
| --- | --- |
| `finalUrl` | Final authorized URL. |
| `readyState` | 0 initially, then lifecycle states 1–4; fetch mode reports 4 only. |
| `status`, `statusText` | HTTP status and text; initially 0/empty. |
| `responseHeaders` | Raw header string, preserving repeated headers. |
| `response` | Text string; parsed JSON (invalid/empty JSON gives `null`); ArrayBuffer; Blob; XML Document for `document`; text for `xml`; or ReadableStream of Uint8Array for `stream`. |
| `responseText` | Decoded text for text/JSON/document/xml; `undefined` for arraybuffer/blob/stream. No incremental text-body guarantee in progress callbacks. |
| `responseXML` | Parsed XML Document for document/xml when DOMParser is available; otherwise `null`. Invalid XML may be a parser-error document. |
| `context` | Original JavaScript context value. |

| Callback | Argument / timing |
| --- | --- |
| `onloadstart`, `onreadystatechange` | Current response state as lifecycle notifications arrive. |
| `onprogress`, `onuploadprogress`, `upload.onprogress` | State plus `loaded`, `total` (0 if unknown), `lengthComputable`. Not delivered in fetch mode. |
| `onload` | Completed response state; HTTP errors still use this callback. |
| `onerror` | Structured error `{error, details, code, name}` for network, permission, serialization, or bridge failure. |
| `ontimeout`, `onabort` | Error information for timeout or cancellation. |
| `onloadend` | Final state on success or error information on failure. |

Callbacks run with `this` set to the response state. Modern preflight validation can throw synchronously before returning a Promise; wrap the entire call in `try/catch` as well as awaiting it. Failures after start reject the Promise; inspect `.code`/`.name` when available. Callback errors use categories such as `not_supported`, `not_permitted`, `timeout`, and `not_succeeded`. The legacy facade reports immediate request validation failures through onerror where possible; malformed callback types may still throw.

XHR bodies are transferred in chunks; the default host has no overall request/response byte cap, keeps the first 256 KiB in memory, then spools to temporary files. This is separate from ordinary bridge-message limits. The JavaScript side still assembles non-stream response bodies in memory. Prefer `stream` for large responses.

**Tampermonkey differences:** transport is CefSharp/host-backed; proxy and partitioned-cookie controls are absent; DOMParser XML/document types and the upload/signal controls are MonkeySharp behavior. Redirect mode does not automatically imply fetch mode. Progress does not promise progressively decoded responseText. Synchronous requests are unsupported in both current documented APIs; do not treat that shared restriction as a MonkeySharp-only difference. Compare the [official XHR reference](https://www.tampermonkey.net/documentation.php?q=GM_xmlhttpRequest).

```javascript
// @grant GM.xmlHttpRequest
// @connect example.com
(async function () {
    const request = GM.xmlHttpRequest({
        url: "https://example.com/", responseType: "text", timeout: 15000,
        onprogress: progress => console.log(progress.loaded, progress.total)
    });
    // To cancel while pending: request.abort();
    const response = await request;
    if (response.status < 200 || response.status >= 300) {
        throw new Error(`HTTP ${response.status}`);
    }
    console.log(response.finalUrl, response.responseText);
})().catch(console.error);
```

## Menus, notifications, and clipboard

### GM.registerMenuCommand(name, callback, accessKey?)

Requires its grant and a menu service. `name` is a non-empty string; `callback` is a function invoked **without arguments** when selected; optional `accessKey` is a string. Modern result: `Promise<number>` after registration. `GM_registerMenuCommand` returns the numeric command ID immediately, logging later failures. IDs belong to the current execution, and registrations are released when it ends. The Demo exposes commands in its script menu and supports Alt+accessKey.

### GM.unregisterMenuCommand(commandId)

Requires its own grant and the same service. Positive integer `commandId`. Modern result: `Promise<boolean>` for host removal; legacy `GM_unregisterMenuCommand` returns a local removal boolean and queues host removal asynchronously. Unknown IDs return false.

**Tampermonkey differences:** the options object (`id`, `title`, `autoClose`, `clickedFrame`, etc.), ID-based updates, frame merging, and MouseEvent/KeyboardEvent callback arguments in the [official menu API](https://www.tampermonkey.net/documentation.php?q=GM_registerMenuCommand) are not implemented.

```javascript
// @grant GM.registerMenuCommand
// @grant GM.unregisterMenuCommand
(async function () {
    const id = await GM.registerMenuCommand("Remove this command", () => {
        GM.unregisterMenuCommand(id).catch(console.error);
    }, "r");
})().catch(console.error);
```

### GM.notification(details)

Requires its grant and a notification service. Accepts an object or a text string (equivalent to `{text}`). Modern result: `Promise<{id: string}>` once the notification is registered, **not when dismissed**. Callback fields must be provided in the object.

| Field | Type / default | Meaning |
| --- | --- | --- |
| `text` | String | Required unless `highlight:true`. |
| `title` | Optional string | Defaults to the script name. |
| `image`, `imageUrl` | Optional URL string | `imageUrl` takes precedence; rendering/fetching is service-specific. |
| `highlight`, `silent` | Booleans, `false` | Requests forwarded to the host; actual effect depends on its implementation. |
| `timeout` | Optional integer milliseconds | Passed to the notification service; host decides supported timing behavior. |
| `onclick`, `ondone` | Optional functions | Invoked without arguments on host click/completion events. |

Legacy overloads: `GM_notification(details, ondone?)`, `GM_notification(text, title?, image?, onclick?)`, and `GM_notification(text, ondone)`. They return `undefined`; delivery failures are logged. Modern calls do not implement a separate second `ondone` argument or the four-argument overload. No remove/update method is exposed.

The Demo closes notifications after a positive timeout or the user's close/click action. It does not give `highlight` or `silent` a separate implementation; passing those fields is not a guarantee of a system highlight or sound policy.

**Tampermonkey differences:** no `tag` replacement, `url` navigation, or Tampermonkey notification handle/Promise-completion contract is supplied. The Demo uses WPF windows, not extension/system notification UI. Compare the [official notification API](https://www.tampermonkey.net/documentation.php?q=GM_notification).

```javascript
// @grant GM.notification
(async function () {
    const registration = await GM.notification({
        title: "MonkeySharp", text: "Operation finished", timeout: 5000,
        onclick: () => console.log("clicked"), ondone: () => console.log("closed")
    });
    console.log("Registered:", registration.id);
})().catch(console.error);
```

### GM.setClipboard(text, type?) / GM_setClipboard(text, type?)

Requires its grant and a clipboard service. `text` must be a string; optional `type` is a MIME string, default `"text/plain"`. Use `"text/html"` for HTML. Modern result: `Promise<undefined>` after the host write. Legacy returns `undefined` and logs failures. The Demo writes Unicode text or CF_HTML on its UI thread and retries clipboard contention briefly.

**Tampermonkey difference:** no info object or completion callback overload; use MIME strings rather than relying on the official API's `"text"`/`"html"` shorthand. Other strings are passed through, not normalized by Core. See the [official clipboard API](https://www.tampermonkey.net/documentation.php?q=GM_setClipboard).

```javascript
// @grant GM.setClipboard
(async function () {
    await GM.setClipboard("Copied by MonkeySharp", "text/plain");
})().catch(console.error);
```

## Tabs and tab state

### GM.openInTab(url, options?) / GM_openInTab(url, options?)

Requires its grant and a tab service. `url` is an absolute HTTP(S) string. Modern `options` supports booleans `active` (default **false**), `insert` (false), `setParent` (false). `insert:true` requests insertion beside the originating tab; layout is host-specific.

Returns a local handle immediately in **both** facades:

| Handle member | Behavior |
| --- | --- |
| `id` | Initially `null`, populated with a host string ID after opening. |
| `closed` | Initially false; becomes true on host closure notification. |
| `onclose` | Assign a function, invoked without arguments when closed. |
| `close()` | `Promise<boolean>`; an early close is queued until opening supplies an ID. Repeated calls reuse the pending result or return false. |

Opening failures are logged, or reject a pending close request; there is no open-readiness Promise. Releasing the execution disposes its handle but does not necessarily close a user tab; the Demo keeps already opened tabs.

Legacy also accepts `GM_openInTab(url, loadInBackgroundBoolean)` and an options `loadInBackground` field; if `active` is absent, it sets `active = !loadInBackground`. Modern calls do not perform those conversions. `incognito` and numeric insertion positions are unsupported. The default inactive behavior agrees with the [official tab API](https://www.tampermonkey.net/documentation.php?q=GM_openInTab); it is not a compatibility difference.

### GM.getTab(), GM.saveTab(value), GM.getTabs()

Require each respective grant and a tab-state service. This is per-installation data associated with **live host tabs**, not page sessionStorage and not an enumeration of arbitrary browser tabs.

| API | Parameters / behavior | Modern result | Legacy contract |
| --- | --- | --- | --- |
| `GM.getTab()` | Read this script's current tab object. | `Promise<object>` | `GM_getTab(callback)` returns undefined; callback `(value, null)` or `(null, error)`. |
| `GM.saveTab(value)` | Required JSON object; replaces saved state. Arrays/primitives are rejected. | `Promise<undefined>` | `GM_saveTab(value)` returns undefined; failures logged; no completion-callback overload. |
| `GM.getTabs()` | Get this script's live tab-state map, keyed by host tab ID. | `Promise<object>` | `GM_getTabs(callback)` returns undefined; same callback/error contract as getTab. |

Legacy getTab/getTabs errors are structured `{error, details, code}` objects. Mutating an object returned by getTab does not save it. The Demo starts with `{}`, preserves tab data across full page navigation, removes closed tabs, and clears stale state on application startup; it does not restore browser sessions.

```javascript
// @grant GM.openInTab
// @grant GM.getTab
// @grant GM.saveTab
// @grant GM.getTabs
(async function () {
    const state = await GM.getTab();
    state.lastAction = "opened help";
    await GM.saveTab(state);
    console.log(await GM.getTabs());
    const tab = GM.openInTab("https://example.com/", { active: true, setParent: true });
    tab.onclose = () => console.log("Help tab closed");
    // Later, while this execution is active: await tab.close();
})().catch(console.error);
```

## Downloads

### GM.download(details) / GM_download(details)

Requires its grant and a download service. The modern call accepts an options object or a URL string. Legacy additionally supports `GM_download(url, name)` and `GM_download(details, onload?, onerror?)`. Use the documented object form for portability.

| Field | Type / default | Behavior |
| --- | --- | --- |
| `url` | Required absolute HTTP(S) string | No Blob/File URL input. |
| `name` | Optional string | Requested filename, ultimately controlled/sanitized by the host. |
| `headers` | String-to-string object, `{}` | Host download headers; validation may differ from XHR. |
| `saveAs` | Boolean, `false` | Request a destination picker. |
| `conflictAction` | `"uniquify"` (default), `"overwrite"`, `"prompt"` | Requested collision behavior. |
| `timeout` | Optional positive integer milliseconds | Download timeout; zero is not valid here. |
| `onload` | Legacy-only function `({id})` | Download completed. |
| `onprogress` | Legacy-only function `({loaded, total})` | Byte progress; total can be null/unknown. |
| `onerror` | Legacy-only function `({error, details, code?})` | Start/failure/cancellation information. |
| `ontimeout` | Legacy-only function `({error: "timeout", details})` | Timeout; falls back to onerror if absent. |

Modern result is a plain **`Promise<DownloadHandle>` resolving when started**, not when completed. Await it to obtain `{id, abort()}`. The Promise itself has no `.abort()`. Legacy returns a local handle immediately; `id` becomes available asynchronously. Handle `abort()` returns `Promise<boolean>` indicating whether a host abort was requested/accepted, not a completion result. Before a legacy handle has an ID it may return false; use terminal callbacks to observe actual outcome.

**Current modern-facade limitation:** `GM.download` does not register callback fields from `details`; they are ignored. It exposes start success/failure and the abort handle, but no script-facing completion/progress notification. Use `GM_download` in `LegacyCompatible` when you need those callbacks. There is currently no completion-observing download facade in `ModernStrict`.

Error categories include `not_enabled` (expired session), `not_permitted` (grant), `not_whitelisted` (host permission), `not_supported`, `not_succeeded`, and `timeout`. Names resemble Tampermonkey's categories, but `not_whitelisted` does not necessarily mean a filename-extension whitelist. Modern host/start failures reject with a plain `{error, details, code}` object, not an Error with `.message`; local validation can still reject a TypeError. Legacy start and later failures arrive through callbacks. A completed/aborted/failed operation is terminal. Navigation or host disposal cancels execution-owned downloads.

The Demo streams to its `Data/Downloads` directory by default, sanitizes names, supports picker/conflict actions, and removes partial files. Its separate HTTP client **does not inherit browser login cookies or credentials**. Core does not apply the XHR `@connect` allowlist to downloads; the host download service and permission policy decide access. There is no implemented `anonymous` option or built-in Tampermonkey extension-whitelist preference.

**Tampermonkey difference:** the [official modern API](https://www.tampermonkey.net/documentation.php?q=GM_download) documents an abortable Promise resolving download details. MonkeySharp instead resolves a start handle; abort the awaited handle and use the legacy facade when completion callbacks are necessary.

```javascript
// @grant GM.download
(async function () {
    const download = await GM.download({
        url: "https://example.com/", name: "example.html"
    });
    console.log("Started:", download.id);
    // To cancel after starting: await download.abort();
})().catch(console.error);
```

For completion notifications in the default legacy profile:

```javascript
// @grant GM_download
const download = GM_download({
    url: "https://example.com/", name: "example.html",
    onload: result => console.log("Finished:", result.id),
    onerror: error => console.error("Download failed:", error),
    ontimeout: error => console.error("Download timed out:", error)
});
// To cancel: download.abort().catch(console.error);
```

## Cookies

### GM.cookie / GM_cookie

Grant `GM.cookie` or `GM_cookie`; all its submethods share that grant. Needs a cookie service. CefSharp's default uses the attached browser request context and can read HttpOnly cookies. Each target URL must match the script's `@match`/`@include` rules and not its exclusions. `@connect` is not a substitute for these URL rules.

| Method | Input / behavior | Result |
| --- | --- | --- |
| `GM.cookie.list(details?)` | Filter cookies; default details `{}`. | `Promise<Cookie[]>` |
| `GM.cookie.set(details)` | Set a named cookie. | `Promise<Cookie>` (or provider null). |
| `GM.cookie.delete(details)` | Delete a named cookie. | `Promise<boolean>` |
| `GM.cookie.addListener(details?, callback)` | Also accepts just callback; receives `(cookie, cause, removed)`. | Positive numeric listener ID immediately; registration failure logged. |
| `GM.cookie.removeListener(listenerId)` | Remove this execution's registration. | `Promise<boolean>` |

| `details` field | Use and default |
| --- | --- |
| `url` | Absolute HTTP(S) string; defaults to the current frame URL. |
| `name` | Exact optional list filter; required non-empty string for set/delete. |
| `domain`, `path` | Optional list filters; for set, domain defaults to URL host, path to `/`. The default CefSharp delete uses URL + name, not exact domain/path selection. |
| `value` | Set value string; omission defaults to empty string. |
| `expirationDate` | Optional integral Unix **seconds**, or a parseable date string; omission creates a session cookie. Prefer Unix seconds for portability. |
| `secure`, `httpOnly` | Set booleans, default false. |
| `sameSite`, `firstPartyDomain` | Optional strings preserved by Core; see default-service limitations below. |

Returned Cookie fields are `name`, `value`, `domain`, `path`, `expirationDate` (Unix seconds or null), `firstPartyDomain`, `hostOnly`, `session`, `secure`, `httpOnly`, and `sameSite`.

Legacy CRUD returns `undefined`: `GM_cookie.list(details?, callback?)` invokes `(cookies, null)` on success or `(null, errorString)` on failure; `set(details, callback?)` and `delete(details, callback?)` invoke no-argument success callbacks or `(errorString)` on failure. Without an error callback, failures are logged. Legacy addListener returns the same numeric ID; legacy removeListener returns undefined and logs failures.

**Default-service limits / Tampermonkey differences:** Cookie `partitionKey` is not implemented; unknown fields are not a partition-selection guarantee. Current CefSharp writes do not apply sameSite/firstPartyDomain to the browser cookie, even though Core accepts/preserves those strings and set results may echo them. Listeners report explicit mutations made through the service (`cause: "explicit"`); they are not a complete feed of page/HTTP cookie changes or expiration events. Unlike the [official Cookie API](https://www.tampermonkey.net/documentation.php?q=GM_cookie), addListener/removeListener are MonkeySharp additions with the three-argument callback above. Host replacements may implement richer behavior.

```javascript
// @grant GM.cookie
(async function () {
    const id = GM.cookie.addListener({}, (cookie, cause, removed) => {
        console.log(cookie.name, cause, removed);
    });
    await GM.cookie.set({ name: "ms_demo", value: "ready", path: "/" });
    console.log(await GM.cookie.list({ name: "ms_demo" }));
    console.log("Deleted:", await GM.cookie.delete({ name: "ms_demo" }));
    await GM.cookie.removeListener(id);
})().catch(console.error);
```

## Request interception

### GM.webRequest(rules, listener?) / GM_webRequest(rules, listener?)

Grant `GM.webRequest` or `GM_webRequest`; needs a webRequest service, supplied by CefSharp by default. `rules` is a rule object or array of objects, each with `selector` and `action`. Registrations are independent and released when the execution ends; creating a new one does not replace earlier handles.

| Input | Supported contract |
| --- | --- |
| String `selector` | URL glob, match-style pattern, or slash-delimited .NET regex. |
| Object `selector` | `include`, `match`, `exclude`, each a string or array of strings; exclusions win. Include/match are alternatives. Exclude-only selectors are permitted. |
| `action: "cancel"` | Cancel a matching request. |
| `action: "https://example.com/new"` | Static HTTP(S) redirect. |
| `action: {from: "...", to: "..."}` | Replace the first case-sensitive literal occurrence of `from` in the URL with `to`; no regex or capture expansion. Both strings must be non-empty. |
| `listener(info, message, details)` | Optional function observing cancel/redirect matches; its return value cannot change the network decision. |

Redirect destinations must be covered by the script's positive URL rules and exclusions, checked for static registration and at evaluation. The default service **blocks** a request if its computed redirect target is invalid or outside those rules. Use narrowly scoped selectors; the default adapter evaluates browser resource callbacks, without Tampermonkey's documented four-resource-type restriction. Later rules in an array have higher priority in MonkeySharp's default service. Requests that already happened before registration cannot be intercepted.

Both facades return `{id, remove()}` immediately. `id` is initially null, later a host string; `remove()` returns `Promise<boolean>`, and can be called before registration completes. Empty rules without a listener are invalid. Registration failures are logged, or reject an already pending removal.

The callback arguments are:

- `info`: `{requestId, url, method, type}`.
- `message`: `"cancel"` or `"redirect"`.
- `details`: `{phase, redirectUrl, error}`; fields depend on the request notification. This reports matching rule observations, not confirmation that a redirected request subsequently loaded successfully.

**Tampermonkey differences:** the [official API](https://www.tampermonkey.net/documentation.php?q=GM_webRequest) uses `{cancel:true}` / `{redirect: ...}` action objects, regex-based redirect substitutions, and `(actionString, "ok"|"error", details)` callbacks. Those contracts are **not** MonkeySharp's: use its literal replacements, action forms, and callback object above. MonkeySharp adds removable handles, does not automatically install static `@webRequest`, and does not expose arbitrary header-edit/authentication actions through this script facade. Tampermonkey's availability also depends on extension manifest/browser version; do not infer MonkeySharp availability from it.

```javascript
// @grant GM.webRequest
// The script also needs @match https://example.com/* for these redirect targets.
const registration = GM.webRequest([
    { selector: "https://example.com/tracker/*", action: "cancel" },
    { selector: "https://example.com/old/*", action: { from: "/old/", to: "/new/" } }
], (info, message, details) => {
    console.log(info.url, message, details.redirectUrl);
});
// Later: registration.remove().catch(console.error);
```

## Window extensions

| Capability | Parameters and behavior | Result |
| --- | --- | --- |
| `unsafeWindow` | Exact grant; exposes the page's global object. It is a value, not a function. | Page global object. |
| `window.close()` | Exact `window.close` grant plus window service; asks the host to close the originating tab/window. | `Promise<boolean>`; false can mean host refusal, such as keeping the last tab. |
| `window.focus()` | Exact `window.focus` grant plus window service; asks the host to focus the originating tab/window. | `Promise<boolean>` for host success. |
| `window.onurlchange` | Exact grant; assign a function or null, or use `window.addEventListener("urlchange", handler)`. | Assignment/event registration; callbacks receive `{url, oldURL}`. |

For URL changes, `pushState`, `replaceState`, `popstate`, and `hashchange` are observed when the URL changes. Use `window.removeEventListener("urlchange", handler)` to remove the same function. URL callbacks run with `this` set to the window facade. The event payload is a plain object, not a native Event; listener options such as `once`/`capture` do not change this custom registration. No grant means no MonkeySharp URL-change subscription. Window close/focus properties are unavailable without their grants and service.

The script's `window` is a facade that binds ordinary native window functions to the page window; it is not an isolated sandbox. The modern facade also exposes `GM.windowClose()`/`GM.windowFocus()` as helper names when the respective `window.close`/`window.focus` capability is enabled. These helpers have the same no-argument `Promise<boolean>` contracts; they are **not separate grant names**.

**Tampermonkey differences:** `unsafeWindow` does not cross an extension-world boundary here; close/focus are asynchronous host decisions. Do not assume native Event payloads or extension isolation from the [official window APIs](https://www.tampermonkey.net/documentation.php?q=window).

```javascript
// @grant window.onurlchange
window.onurlchange = change => console.log(change.oldURL, "->", change.url);
const onRoute = change => console.log("Current route:", change.url);
window.addEventListener("urlchange", onRoute);
// Later: window.removeEventListener("urlchange", onRoute);
```

## Tampermonkey compatibility checklist

| Area | Porting action |
| --- | --- |
| Runtime boundary | Treat source and page as trusted page-world code; no extension sandbox or guaranteed CSP bypass. |
| Grants and profiles | `GM.info` and `GM_info` are always available without grants. Declare other used APIs; choose canonical modern names for ModernStrict. |
| Timing / SPA | Do not require guaranteed document-start or automatic reruns on history changes. |
| Match subdomains | Add the bare domain separately when using `*.domain` match patterns. |
| Stored values | Use JSON data; await modern writes for failures; account for bounded mirrors and local-listener limitations. |
| Resources | Use exact `GM.getResourceURL`; expect a data URL and host-provided resource availability. |
| XHR | Check unsupported proxy/partition/Blob-URL controls; adapt progress/XML behavior; inspect HTTP status explicitly. |
| Menu / clipboard | Use string access keys and MIME strings; remove unsupported options objects/callback overloads. |
| Notifications | Use supported detail fields; modern resolution is registration, not dismissal. |
| Tabs | Both facades return immediate handles; use close/onclose; private mode and numeric insertion positions are absent. |
| Downloads | Await the start handle, then call its abort; completion callbacks require legacy GM_download; Demo downloads lack browser credentials. |
| Cookies | No partitionKey semantics; default SameSite/first-party writes and change feeds are limited. |
| webRequest | Rewrite action objects and callbacks; explicitly register runtime rules and retain the removal handle. |
| Other manager APIs | `GM.audio`/`GM_audio` and extension-specific content/deployment APIs are not built in. Unknown grants do not add them. |

This is a list of supported contracts and known differences, not a promise that every Tampermonkey script can run unchanged. Prefer testing each feature against the actual embedding application.

## Errors and troubleshooting

Most modern bridge operations reject with errors carrying `.code` and `.message`; local validation can throw/reject `TypeError`. Modern download host/start failures instead reject plain `{error, details, code}` objects without `.message`. Specialized XHR and legacy-download callbacks use the documented structured errors, legacy Cookie errors are strings, and legacy tab-read errors are objects. Legacy fire-and-forget operations commonly log failures instead of throwing them back to the caller.

| Code | Meaning / action |
| --- | --- |
| `MSP001_PROTOCOL_VERSION`, `MSP002_MALFORMED_MESSAGE` | Bridge/version mismatch or malformed messages; report to the application maintainer. |
| `MSP003_SESSION_EXPIRED` | Old document/execution; stop using handles after navigation. |
| `MSP004_GRANT_DENIED` | Method not granted; correct exact metadata spelling. |
| `MSP005_PERMISSION_DENIED` | Host policy, XHR connect allowlist, or target URL rules deny the operation. |
| `MSP006_NOT_SUPPORTED` | Missing implementation/service or unsupported control. |
| `MSP007_INVALID_PARAMS` | Invalid type, missing required field, or bad value. |
| `MSP008_PAYLOAD_TOO_LARGE` | Ordinary bridge/resource limit exceeded. |
| `MSP009_TIMEOUT`, `MSP010_CANCELED` | Timeout or cancellation; handle the terminal outcome. |
| `MSP999_INTERNAL` | Provider/internal failure; consult host diagnostics. |

Defaults: ordinary bridge requests/responses are limited to 1 MiB each, resources/dependencies to 10 MiB, ordinary requests to 30 seconds, and pending calls to 64 per document. Hosts can override these limits. Chunked XHR transfers and their long-running execute operation use their own controls rather than applying a 1 MiB total-body cap. Bootstrap snapshots have separate limits described above.

| Symptom | Check |
| --- | --- |
| Script never runs | Enabled installation, positive URL match, exclusions, iframe rules, run-at, metadata errors; explicit grants also require host trust opt-in (`MSR201_BRIDGE_INTEGRITY_REQUIRED`). |
| `GM` or one method is undefined | Grant state, exact spelling, profile, and host service. Missing APIs are usually absent, not stubs. |
| Only synchronous storage/resource APIs are missing | Snapshot options, budget/timeout; `MSC410_COMPATIBILITY_BOOTSTRAP_FAILED`, `MSC411_COMPATIBILITY_SNAPSHOT_DEGRADED`, `MSC412_COMPATIBILITY_BOOTSTRAP_LIMIT`. |
| A legacy write seemed to succeed but data reverted | Async commit failed; inspect `MSC413_COMPATIBILITY_MUTATION_FAILED`; use awaited modern writes. |
| A dependency is missing | Host dependency provider and `MSR400_DEPENDENCY_RESOLUTION_FAILED` / `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE`. |
| Code runs too late or in the wrong world | document-start is best effort (`MSR100`/`MSR101`); sandbox/unwrap requests cannot create isolation (`MSR212`/`MSR213`). |
| XHR fails after a redirect | Allow every redirect destination with `@connect`; check host policy. |
| Download fails despite browser login | Demo downloader is a separate HTTP client; use the application's authenticated/native download path if provided. |
| Async callback stopped after navigation | The old execution was released; register again in the new document. |
| JavaScript exception | DevTools console and host `MSR300_SCRIPT_EXCEPTION`; catch asynchronous work explicitly. |

## Reference sources

MonkeySharp behavior was checked against the [grant catalog](../Mzying2001.MonkeySharp.Core/Compatibility/UserScriptGrantCatalog.cs), [JavaScript facade](../Mzying2001.MonkeySharp.Core/Js/Init.js), [Core API providers](../Mzying2001.MonkeySharp.Core/Apis/), [metadata parser](../Mzying2001.MonkeySharp.Core/Parsing/UserScriptMetadataParser.cs), [runtime info builder](../Mzying2001.MonkeySharp.Core/Runtime/UserScriptEngine.cs), [CefSharp services](../Mzying2001.MonkeySharp.CefSharp/), and [JavaScript contract tests](../Mzying2001.MonkeySharp.Core.Tests/JavaScript/). The [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md) documents its concrete persistence and interaction services. Use these sources when upgrading the runtime or replacing host services.
