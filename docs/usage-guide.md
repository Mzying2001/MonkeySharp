# MonkeySharp Userscript Guide

**English** | [简体中文](usage-guide.zh-CN.md)

This guide describes the JavaScript userscript surface in the current MonkeySharp 3 source tree. It covers writing scripts, every built-in script API, and differences from Tampermonkey. Applications can change services, permissions, and limits. For .NET integration, see the [integration reference](integration-reference.md); for installation and UI operations, see the [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md).

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

The header begins with `// ==UserScript==` and ends with `// ==/UserScript==`. Put it first for portability. The default parser allows a preamble within 128 lines/64 Ki UTF-16 characters; Metadata errors prevent installation; warnings remain available to the host. For known single-valued keys, the first occurrence wins and duplicates produce a warning; repeat collection fields on separate lines. Unknown entries are preserved but do not acquire runtime behavior.

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

`@match https://example.com/*` covers that host; `@match https://*.example.com/*` covers the bare `example.com` and all its subdomains, following the [Tampermonkey match contract](https://www.tampermonkey.net/documentation.php?q=include). The same host wildcard rule applies to `@exclude-match`. `*://` and `http*://` cover HTTP/HTTPS; explicit ports are checked. URL hosts are IDN-normalized, fragments are ignored, and query strings participate in matching.

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

The host fetches and caches dependencies. A script declaring `@require` is skipped if its provider is absent; dependency failure never silently executes the main source alone. Resource APIs require a resource provider.

The Demo's manual updater checks `@updateURL` before `@downloadURL`, and installs from `@downloadURL` before the check source. Candidates require the same non-empty name/namespace and a newer downloaded version. Applying an update preserves installation identity, enabled state, and stored values. Its downloader has no browser login cookies. See the [update and integrity reference](integration-reference.md#resource-integrity-and-update-checks).

## Execution, grants, and compatibility

There is one contract. [Pinned Tampermonkey 5.5.1 evidence](compatibility/api-contract.md)
confirms that missing grants, `none`, unknown grants and info-only declarations
expose `GM_info` and `GM.info`. These declarations remain distinct in metadata.
`unsafeWindow` is absent with `none`; otherwise it refers to the page global.

Legacy `GM_*` names require their exact declarations. Modern storage, resource,
menu and tab-data APIs are granted as families, matching the pinned runtime:
`GM_getValue` also enables modern storage writes, but does not expose the
undeclared `GM_setValue` global. Other families remain ungranted. Providers and
host policy still determine whether operations are available and permitted.

Calling style does not force strictness or select a world. Source directives
control strictness. Top-level this is the window facade, except `none` uses a
separate object receiver as observed in 5.5.1. All production execution remains
in the trusted page world; the CDP isolated-world experiment is not a privileged
production sandbox. Document-start remains best effort, and guaranteed timing
requirements fail explicitly.

Legacy synchronous storage/resource reads require complete snapshots. Preparation
has a 1 MiB default budget and a five-second timeout. Host-backed UI APIs require
their services. [Integration details](integration-reference.md#api-contract-and-grants)
describe trust, providers and actual environment facts.

## API index

Each row requires its capability family or exact legacy declaration unless stated otherwise. `Promise<T>` means use `await`; an immediate ID/handle is not a readiness guarantee for its background registration. Legacy names require their exact declarations. Details, errors, and examples follow.

| Capability / modern member | Legacy name | Modern result | Implementation |
| --- | --- | --- | --- |
| `GM.info` | `GM_info` | Frozen object, not a function | Core |
| `GM.log` | `GM_log` | `Promise<undefined>` | Core logging sink |
| `GM.getValue` | `GM_getValue` | `Promise<value>` | Value store |
| `GM.setValue` | `GM_setValue` | `Promise<undefined>` | Value store |
| `GM.deleteValue` | `GM_deleteValue` | `Promise<undefined>` | Value store |
| `GM.getValues` | `GM_getValues` | `Promise<object>` | Value store |
| `GM.setValues` | `GM_setValues` | `Promise<undefined>` | Value store |
| `GM.deleteValues` | `GM_deleteValues` | `Promise<undefined>` | Value store |
| `GM.listValues` | `GM_listValues` | `Promise<string[]>` | Value store |
| `GM.addValueChangeListener` | `GM_addValueChangeListener` | `Promise<number>` | Core |
| `GM.removeValueChangeListener` | `GM_removeValueChangeListener` | `Promise<undefined>` | Core |
| `GM.addStyle` | `GM_addStyle` | `Promise<HTMLStyleElement>` | Page DOM |
| `GM.addElement` | `GM_addElement` | `Promise<Element>` | Page DOM |
| `GM.getResourceText` | `GM_getResourceText` | `Promise<string>` | Resource provider |
| `GM.getResourceUrl` | `GM_getResourceURL` | `Promise<string>` | Resource provider |
| `GM.xmlHttpRequest` | `GM_xmlhttpRequest` | Abortable `Promise<Response>` | CefSharp default |
| `GM.registerMenuCommand` | `GM_registerMenuCommand` | `Promise<ID>` | Menu service |
| `GM.unregisterMenuCommand` | `GM_unregisterMenuCommand` | `Promise<undefined>` | Menu service |
| `GM.notification` | `GM_notification` | `Promise<boolean>` | Notification service |
| `GM.setClipboard` | `GM_setClipboard` | `Promise<undefined>` | Clipboard service |
| `GM.openInTab` | `GM_openInTab` | `Promise<TabHandle>` | Tab service |
| `GM.getTab` | `GM_getTab` | `Promise<object>` | Tab-state service |
| `GM.saveTab` | `GM_saveTab` | `Promise<undefined>` | Tab-state service |
| `GM.getTabs` | `GM_getTabs` | `Promise<object>` | Tab-state service |
| `GM.download` | `GM_download` | `Promise<DownloadDetails> & {abort()}` | Download service |
| `GM.cookie` | `GM_cookie` | Namespace with list/set/delete | CefSharp default |
| `GM.webRequest` | `GM_webRequest` | Removable handle immediately | CefSharp default |
| `unsafeWindow` | Same name | Page global object | Exact grant |
| `window.close`, `window.focus` | Same names | `undefined` | Window service |
| `window.onurlchange` | Same name | Property/event registration | Core |

## Information and logging

### GM.info / GM_info

Read-only, recursively frozen installation snapshot. `GM_info === GM.info`. Both names are always available without a grant, under the single contract. Scripts with no grants, `@grant none`, or only info grants need no bridge binding or handshake. Explicit info grants remain supported; metadata retains the original declarations. There are no parameters and no Promise. Missing optional metadata is generally `null`.

| Fields | Meaning |
| --- | --- |
| `scriptKey` | Stable installation identifier. |
| `name`, `namespace`, `description` | Top-level metadata conveniences. |
| `version` | Actual MonkeySharp manager version; script version is `script.version`. |
| `grants`, `grantDeclarationState` | Original declared names and `Missing`, `ExplicitNone`, or `ExplicitList`. Grant aliases are normalized internally; these lists retain declaration spelling. |
| `scriptHandler`, `sandboxMode` | MonkeySharp identity and actual execution mode. |
| `downloadMode`, `isIncognito`, `scriptWillUpdate` | Actual download backend and host context/update policy. Unknown context/policy fields are omitted. |
| `scriptMetaStr`, `scriptUpdateURL` | Raw metadata header and declared update URL. |
| `compatibility` | `contractVersion: 2`, `handler: "MonkeySharp"`, `executionWorld: "page"`, `isolatedWorld: false`; `fields` records field provenance. |
| `script` | Metadata object detailed below. |

`script` contains `name`, `namespace`, `version`, `description`, `author`, `copyright`, `name_i18n`, `description_i18n`, `icon`, `icon64`, `homepage`, `supportURL`, `downloadURL`, `updateURL`, `fileURL` (installation source), `header`, `grant`, `connects`, `matches`, `includes`, `excludes` (including exclude-match entries), `resources`, `antifeatures`, `webRequest`, `run-at`, and `unwrap`. `lastModified` is installation-update Unix **milliseconds**; unsupported manager-only `position` and `blockers` fields are omitted. `resources` entries are `{name, url, integrity: [{algorithm, digest}]}`; antifeatures are grouped by type, then locale (`default` for unlocalized). `webRequest` is `null` when absent. `options` contains `sandbox`, `noframes`, `unwrap`, and `run_at`.

**Tampermonkey difference:** this is MonkeySharp's snapshot, with explicit provenance and actual environment facts; it does not promise every [Tampermonkey GM_info field](https://www.tampermonkey.net/documentation.php?q=GM_info), live browser settings, or extension update state.

### GM.log(value) / GM_log(value)

`value` is cloned using `JSON.stringify`/`JSON.parse`; top-level `undefined`, functions, symbols, BigInt, and cycles are invalid. This logging rule is separate from the storage value encoding below. Modern calls return `Promise<undefined>` after delivery to the host logging callback. If no sink is configured, delivery has no visible output. `GM_log` returns `undefined`, additionally writes to the page console, and reports asynchronous host failures to the console. Only one value is accepted; this is not console.log's variadic formatting API.

```javascript
// @grant GM.log
(async function () {
    await GM.log({ script: GM.info.name, version: GM.info.version });
})().catch(console.error);
```

## Values and change listeners

Values are isolated by installation `ScriptKey`, shared by its active tabs/frames, and independent of page localStorage. Reinstalling as a different installation need not reuse values. The Demo preserves values when updating an existing installation and removes them on uninstall; other hosts choose persistence policy.

Keys are strings, including the empty string. Stored values may be `undefined`, `null`, strings, numbers (including `NaN` and positive/negative infinity), booleans, arrays, and objects. A stored `undefined` is an existing key: it appears in `listValues` and does not fall back to a default. Only `deleteValue`/`deleteValues` removes a key.

Nested values follow the behavior checked against Chromium Tampermonkey 5.5.1: `undefined` object properties are omitted; array `undefined` values and holes become `null`; nested non-finite numbers become `null`. Top-level negative zero becomes zero. Objects are serialized from their own enumerable string properties without calling `toJSON`, so a `Date` becomes `{}`. Stored objects are detached copies; edit and save them explicitly. Functions, symbols, BigInt, and cycles are rejected as stored values, including inside objects/arrays.

Defaults stay in JavaScript and are returned only for absent keys. They may be any value, including functions, symbols, BigInt, or cyclic objects; their references are preserved. Bulk default properties are captured when the API is called, before the asynchronous lookup. A defaults object's explicit `undefined` property is included in the bulk result. Promise resolution still follows JavaScript's usual thenable handling.

| Signature | Parameters and behavior | Result |
| --- | --- | --- |
| `GM.getValue(key, defaultValue?)` | Return the stored value when present, otherwise the default, or `undefined` if omitted. | `Promise<value>` |
| `GM.setValue(key, value)` | Encode a detached value before queuing its commit. | `Promise<undefined>` |
| `GM.deleteValue(key)` | Delete if present; absent keys are a no-op. | `Promise<undefined>` |
| `GM.getValues(keysOrDefaults?)` | Read an array of keys (absent keys are omitted), or an object of defaults. Omitted, `null`, or `undefined` reads all values; `[]` and `{}` return empty objects. The result has no prototype. | `Promise<object>` |
| `GM.setValues(values)` | Set the object's own enumerable string keys; validate and encode all values before changing storage. | `Promise<undefined>` |
| `GM.deleteValues(keys)` | Delete an array of keys, ignoring duplicates and absent keys. | `Promise<undefined>` |
| `GM.listValues()` | Return keys in JavaScript property enumeration order: array-index keys first, then other keys in insertion order. Replacing a key preserves its position; deleting and reinserting moves it to the end of the non-index keys. | `Promise<string[]>` |
| `GM.addValueChangeListener(key, callback)` | Register `callback(key, oldValue, newValue, remote)`; absent/deleted values are `undefined`. Host registration failures reject. | `Promise<number>` |
| `GM.removeValueChangeListener(listenerId)` | Remove this execution's positive-integer listener ID; unknown IDs are a no-op. | `Promise<undefined>` |

Modern storage belongs to one authorized family; each legacy global requires its exact declaration. Listeners cover active executions of the same installation. Every committed change notifies each registered listener once, with `remote: false` for the originating execution and `true` for other executions or host-originated changes. This applies with or without a mirror. Identical encoded writes and deletion of absent keys do not notify. Creating or deleting a stored `undefined` does notify, even though both callback values can be `undefined`. Callbacks run after commit; they may arrive after the write Promise resolves. Listeners end with the execution.

Legacy signatures use the same parameters: `GM_getValue`/`GM_getValues`/`GM_listValues` return values immediately; `GM_setValue`/`GM_deleteValue`/`GM_setValues`/`GM_deleteValues`/`GM_removeValueChangeListener` return `undefined`; `GM_addValueChangeListener` returns a numeric ID immediately. Synchronous value reads/writes require a complete storage mirror. Listener aliases do not require a mirror. Writes update the mirror immediately and queue asynchronous commits. Failed commits roll back/reconcile and emit `MSC413_COMPATIBILITY_MUTATION_FAILED`; a successful synchronous return does not mean persistence. Legacy listener registration failures remove the local handler and report the failure asynchronously.

Modern writes share this commit queue and their Promises expose host completion/failure. Each bulk write/delete uses one bridge request and commits keys sequentially. A storage failure stops the batch and preserves previously committed keys and their notifications. Validation failures change no keys. Reconciliation reapplies later queued mutations. Empty batches are valid.

```javascript
// @grant GM.getValues
// @grant GM.setValues
// @grant GM.deleteValues
(async function () {
    await GM.setValues({ theme: 'dark', optional: undefined });
    const settings = await GM.getValues({ theme: 'light', optional: 'fallback', absent: undefined });
    console.log(settings.optional === undefined, Object.prototype.hasOwnProperty.call(settings, 'absent')); // true, true
    await GM.deleteValues(['theme', 'optional']);
})().catch(console.error);
```

**Tampermonkey compatibility:** value conversion, defaults, and storage API return types follow the [values reference](https://www.tampermonkey.net/documentation.php?q=GM_values) and the checked 5.5.1 facade. MonkeySharp intentionally waits for host commit confirmation and notifies after commit; Tampermonkey updates local state and notifies immediately. Snapshot limits and rejection of unsupported stored types remain MonkeySharp constraints.

```javascript
// @grant GM.getValue
// @grant GM.setValue
// @grant GM.deleteValue
// @grant GM.listValues
// @grant GM.addValueChangeListener
// @grant GM.removeValueChangeListener
(async function () {
    const id = await GM.addValueChangeListener("settings", (key, oldValue, newValue, remote) => {
        console.log(key, oldValue, newValue, remote);
    });
    const settings = await GM.getValue("settings", { enabled: true });
    settings.enabled = !settings.enabled;
    await GM.setValue("settings", settings);
    console.log(await GM.listValues());
    await GM.removeValueChangeListener(id);
    await GM.deleteValue("settings");
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

### GM.getResourceUrl(name) / GM.getResourceUrl(name) / GM_getResourceURL(name)

Same prerequisites and name rules, with its own grant. Modern result: `Promise<string>` containing a **data URL**, `data:<media-type>;base64,<bytes>`; legacy returns that URL immediately from its snapshot. It is not the download URL and is not a revocable object URL. There is no supported `isBlobUrl` parameter. Integrity and size limits apply to both resource APIs.

The modern spelling is `GM.getResourceUrl`; the official legacy spelling is `GM_getResourceURL`. Either resource capability enables the modern resource family. Legacy globals require their exact declaration and complete snapshots; `GM.getResourceURL` is absent.

Use the data-URL contract; do not assume browser-extension/object-URL behavior or additional options from other managers. See the [official resource reference](https://www.tampermonkey.net/documentation.php?q=GM_getResource).

Replace the badge URL in this fragment with an actual reachable image resource; the example domain does not guarantee a favicon at that path.

```javascript
// @resource badge https://example.com/favicon.ico
// @grant GM.getResourceUrl
(async function () {
    const image = document.createElement("img");
    image.src = await GM.getResourceUrl("badge");
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

`@connect self` checks scheme, host, and port. Both `example.com` and `*.example.com` allow the domain itself and all its subdomains. IPs and localhost are exact. URL declarations additionally constrain scheme/port; their path is not an allowlist restriction. An omitted allowlist denies even same-origin XHR. Original and redirected URLs must all pass the allowlist and host permission policy.

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

### GM.registerMenuCommand(name, callback, options_or_accessKey?)

Requires a menu grant and service. The third argument is an access-key string or `{id, accessKey, title, autoClose}`. `id` is a positive integer or nonempty string; it updates that execution's existing registration, or creates one. Without an ID, a numeric ID is allocated. Modern returns `Promise<number|string>` after registration; legacy returns the ID immediately. Updates and removals are queued in call order. A click supplies a renderer-created MouseEvent (`type: "click"`), without native pointer coordinates or keyboard modifiers. Callback exceptions are reported independently.

Demo merges commands from the same installation and tab when name, title and accessKey match. Selection invokes each registered frame's callback. `title` becomes the tooltip; `autoClose:false` keeps the popup open (a merged item stays open if any subscriber requests it). Unregistering or ending one frame removes only that frame's subscription. Execution end removes its commands. The later `clickedFrame` option is explicitly unsupported by the pinned 5.5.1 contract.

### GM.unregisterMenuCommand(commandId)

Accepts the numeric/string ID. Modern resolves `undefined`; legacy returns `undefined` immediately. Unknown IDs are harmless. No removal boolean is exposed. The menu family grant supplies both modern methods; legacy globals still require their exact declarations.

```javascript
const id = await GM.registerMenuCommand('Action', event => console.log(event.type), {title:'Run action',autoClose:false});
await GM.registerMenuCommand('Updated action', () => {}, {id});
await GM.unregisterMenuCommand(id);
```

### GM.notification(details, ondone?)

Both forms accept `(details, ondone?)` or `(text, title?, image?, onclick?)`; `(text, ondone)` also works. Modern returns `Promise<boolean>` and waits for interaction to finish: true after click, false after dismissal, timeout, replacement or highlight-only completion. Legacy returns undefined. `ondone` runs before Promise settlement. Callbacks are registered before host UI starts, and duplicate click/close signals cannot settle twice.

Options include text (required unless highlight), title (script name by default), image/imageUrl, tag, url, highlight, silent, timeout (nonnegative milliseconds), onclick and ondone. Reusing a nonempty tag replaces that installation's notification in the host. `onclick` receives a cancelable click Event; synchronous `event.preventDefault()` suppresses URL navigation. Callback errors are reported separately and still acknowledge the click. The host waits for this acknowledgment before its default action; after five seconds without acknowledgment it proceeds with the default action. URLs must be absolute HTTP(S) and pass host authorization.

Demo uses owned WPF windows. It selects/activates the originating tab for highlight; highlight without text finishes without a window. Silent suppresses sound; positive timeout closes the window. A normal URL click opens a tab after the callback. Notifications without url/tag close on document end. Those with url/tag remain until completion or host disposal, with old-document callbacks muted; the default click action can still run. Tags are scoped to an installation in one host, rather than a global OS notification service.

```javascript
const clicked = await GM.notification({text:'Ready',tag:'status',silent:true,url:'https://example.com/',
    onclick:event => event.preventDefault(), ondone:() => console.log('Closed')});
console.log('Clicked:', clicked);
```

### GM.setClipboard(data, info?, callback?) / GM_setClipboard(data, info?, callback?)

Data must be a string. Info accepts `"text"`, `"html"`, `"text/plain"`, `"text/html"`, or `{type:'text'|'html', mimetype:'text/plain'|'text/html'}`; mimetype takes precedence. Default is text. Other formats fail before writing. The optional callback runs only after the host write succeeds. Modern resolves undefined after writing and callback invocation; legacy returns undefined immediately. Callback errors do not undo the successful write or reject the modern call.

Demo writes Unicode text and, for HTML, CF_HTML with UTF-8 byte offsets on its STA dispatcher, retaining the clipboard-contention retry loop. It preserves HTML rather than silently converting it to plain text. Real E2E verifies both formats and restores the previous clipboard afterward.

```javascript
await GM.setClipboard('<b>Hello</b>', {type:'html',mimetype:'text/html'});
GM_setClipboard('Copied', 'text', () => console.log('Written'));
```


## Tabs and tab state

### GM.openInTab(url, options?) / GM_openInTab(url, options?)

Requires its grant and a tab service. `url` is an absolute HTTP(S) string. Modern `options` supports booleans `active` (default **false**), `insert` (false), `setParent` (false). `insert:true` requests insertion beside the originating tab; layout is host-specific.

Modern returns `Promise<TabHandle>` after opening; legacy returns a local handle immediately:

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

Requires its grant and a download service. Both forms accept an options object or `(url, name?)`, where `url` is an absolute HTTP(S) string. Blob/File inputs remain unsupported.

| Field | Type / default | Behavior |
| --- | --- | --- |
| `url` | Required HTTP(S) string | Initial target and redirects pass host authorization. |
| `name` | Optional string | Suggested filename; the host selects and sanitizes the destination. |
| `headers` | String-to-string object, `{}` | Uses the shared browser network service. |
| `anonymous` | Boolean, `false` | Omit browser cookies and credentials. |
| `saveAs` | Boolean, `false` | Request a host destination picker. |
| `conflictAction` | `"uniquify"`, `"overwrite"`, `"prompt"` | Default is uniquify. Picker and conflict options require browser mode. |
| `timeout` | Positive integer milliseconds | Covers destination selection, transfer and commit; the file service defaults to ten minutes. |
| `onload` | Function `({url, name, loaded})` | Runs after successful final file commit. |
| `onprogress` | Function `({loaded, total})` | Byte progress; total can be null. |
| `onerror` | Function `({error, details, code?})` | Start, transfer, cancellation or disk failure. |
| `ontimeout` | Function | Timeout; falls back to onerror. |

Modern `GM.download` immediately returns the original completion **Promise with `abort()`**. It resolves only after the file is flushed, closed and atomically moved/replaced into its final destination. Its completion details are the supported subset `{url, name, loaded}` (final URL, actual filename, committed bytes), without local paths or internal IDs. Legacy immediately returns `{abort()}`. Both abort methods return `undefined`, work before host acknowledgment, and are harmless after a terminal state. Both forms support callbacks; a callback exception is reported separately and cannot turn a committed download into failure. Each operation settles once.

Errors use `{error, details, code?}`: `not_enabled`, `not_permitted`, `not_whitelisted`, `not_supported`, `not_succeeded`, or `timeout`. Host failures reject the modern Promise and invoke the callback; legacy failures use callbacks. A picker cancellation and explicit abort are `not_succeeded`. Unsupported Blob/File inputs and invalid options fail before network work.

Demo GM downloads use the exact CEF HTTP service and RequestContext used by GM XHR, including browser login credentials, anonymous behavior, redirect checks and configured webRequest composition. They stream through a bounded buffer into temporary files, then commit to `Data/Downloads` or a picker destination. Cancellation/failure removes partials before the terminal event. Download authorization uses the host policy, rather than XHR's `@connect` list; there is no extension-whitelist preference.

Accepted downloads are host tasks. Navigation, execution end, script disable or uninstall mute document callbacks while accepted transfers continue under their captured authorization and redirect policy. Host disposal cancels tasks; the host must drain them before disposing CEF/network services. An explicit abort before completion cancels the task. Unaccepted or invalid calls do no transfer work.

```javascript
// @grant GM.download
const pending = GM.download({ url: 'https://example.com/file', name: 'file.bin' });
// pending.abort(); // optional, including immediately after the call
pending.then(details => console.log('Committed:', details.name, details.loaded), console.error);

// @grant GM_download
const handle = GM_download({
    url: 'https://example.com/file', name: 'file.bin',
    onload: details => console.log('Committed:', details.name),
    onerror: error => console.error(error)
});
// handle.abort();
```

## Cookies

### GM.cookie / GM_cookie

Requires a cookie grant and service. Modern GM.cookie exposes list/set/delete; legacy GM_cookie is a function namespace with those three methods. There are no addListener/removeListener extensions. Target URLs must satisfy the script's match/include/exclude rules and host permission policy before backend access; @connect does not replace these checks. Results are filtered again in Core by URL, host-only/domain, path and secure scope.

| Method | Modern result | Legacy result/callback |
| --- | --- | --- |
| list(details?) | Promise of actual Cookie[] | undefined; callback(cookies, null), or callback(null, errorString) |
| set(details) | Promise of undefined after verified write | undefined; callback() or callback(errorString) |
| delete(details) | Promise of undefined after verified removal | undefined; callback() or callback(errorString) |

Omitted details/url use the current frame URL. Name is an optional exact list filter and a required nonempty set/delete string. Domain/path are optional exact list filters. Set without domain creates a host-only cookie; an explicit domain must include the target host and creates a domain cookie when accepted by Chromium. Default path is the URL directory (for /nested/page it is /nested), matching the pinned reference. Explicit paths start with /. Value defaults to an empty string. Expiration is optional integral Unix seconds; omission creates a session cookie. Secure and httpOnly default false. SameSite accepts unspecified, no_restriction, lax or strict, mapped to the CDP modes rather than reflected back from input.

List returns actual name/value/domain/path, hostOnly, session, secure, httpOnly and normalized sameSite. Persistent cookies include expirationDate in Unix seconds; session cookies omit it. Unknown first-party/partition state is not invented. Set checks the protocol result and reads back the complete selected identity and attributes; a policy-rejected or silently discarded write is an error. An expired set verifies absence. Delete selects one visible matching identity (longest path first; host-only first on a tie), sends its exact domain/path, then verifies absence. Explicit domain/path further constrain deletion; it never broadly removes every cookie with the same name. Deleting an absent cookie is harmless.

The default backend uses the host's shared CDP session, which belongs to the attached browser's actual RequestContext. It keeps one session throughout each operation and never detaches another consumer's observer. Navigation can cancel a document's calls; detach/disposal invalidate the native session. `new CefSharpCookieService(host)` is available to direct hosts; the former request-context-only constructors are removed. A custom ICookieService implements only CRUD and must return actual verified state.

partitionKey, cookiePartition and firstPartyDomain are explicitly unsupported before effects on all current versions, including 84. Partition support remains a later enhancement. Partitioned cookies are excluded from this unpartitioned backend rather than accidentally changed. Legacy callback failures are reported once, independently of the completed operation. Calling the legacy namespace itself is unsupported; use its three documented methods.

```javascript
await GM.cookie.set({name:'session',value:'ready',sameSite:'lax',path:'/'});
console.log(await GM.cookie.list({name:'session'}));
await GM.cookie.delete({name:'session'}); // undefined, no removal boolean
GM_cookie.list({}, (cookies, error) => console.log(cookies, error));
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
| `window.close()` | Exact `window.close` grant plus window service; asks the host to close the originating tab/window. | `undefined`; host policy may refuse the action. |
| `window.focus()` | Exact `window.focus` grant plus window service; asks the host to focus the originating tab/window. | `undefined`; host policy may refuse the action. |
| `window.onurlchange` | Exact grant; assign a function or null, or use `window.addEventListener("urlchange", handler)`. | Assignment/event registration; callbacks receive `{url, oldURL}`. |

For URL changes, `pushState`, `replaceState`, `popstate`, and `hashchange` are observed when the URL changes. Use `window.removeEventListener("urlchange", handler)` to remove the same function. URL callbacks run with `this` set to the window facade. The event payload is a plain object, not a native Event; listener options such as `once`/`capture` do not change this custom registration. No grant means no MonkeySharp URL-change subscription. Window close/focus properties are unavailable without their grants and service.

The script window is a facade that binds native functions to the page window. Grant-gated close/focus return undefined; no GM.windowClose/GM.windowFocus aliases are exposed.

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
| Grants | Info is implicit; legacy globals require exact declarations, while modern API families follow the pinned 5.5.1 evidence. |
| Timing / SPA | Do not require guaranteed document-start or automatic reruns on history changes. |
| Match subdomains | `*.domain` match patterns include the bare domain and all its subdomains. |
| Stored values | Use supported storage values; await modern writes for failures; account for bounded mirrors and post-commit notifications. |
| Resources | Use `GM.getResourceUrl` or `GM.getResourceUrl` with either grant; expect a data URL and host-provided resource availability. |
| XHR | Check unsupported proxy/partition/Blob-URL controls; adapt progress/XML behavior; inspect HTTP status explicitly. |
| Menu / clipboard | Options and ID updates, interaction-completion notifications, and text/HTML clipboard info/callback overloads. |
| Notifications | Use supported detail fields; modern resolution is registration, not dismissal. |
| Tabs | Modern returns a Promise; legacy returns an immediate handle; use close/onclose; private mode and numeric insertion positions are absent. |
| Downloads | Immediately returns an abortable completion Promise; Demo uses the shared browser network context and commits the file before completion. |
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
| Download fails despite browser login | Check the actual RequestContext, anonymous option, redirect authorization, and host permission policy. |
| Async callback stopped after navigation | The old execution was released; register again in the new document. |
| JavaScript exception | DevTools console and host `MSR300_SCRIPT_EXCEPTION`; catch asynchronous work explicitly. |

## Reference sources

MonkeySharp behavior was checked against the [grant catalog](../Mzying2001.MonkeySharp.Core/Compatibility/UserScriptGrantCatalog.cs), [JavaScript facade](../Mzying2001.MonkeySharp.Core/Js/Init.js), [Core API providers](../Mzying2001.MonkeySharp.Core/Apis/), [metadata parser](../Mzying2001.MonkeySharp.Core/Parsing/UserScriptMetadataParser.cs), [runtime info builder](../Mzying2001.MonkeySharp.Core/Runtime/UserScriptEngine.cs), [CefSharp services](../Mzying2001.MonkeySharp.CefSharp/), and [JavaScript contract tests](../Mzying2001.MonkeySharp.Core.Tests/JavaScript/). The [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md) documents its concrete persistence and interaction services. Use these sources when upgrading the runtime or replacing host services.
