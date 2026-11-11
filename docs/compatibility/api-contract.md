# Tampermonkey contract baseline

This is the target contract; each capability also has a verified scope and explicit limits in the [compatibility index](README.md). The minimum engine remains CefSharp 84.4.10, with 121.3.70 and 151.3.240 regression targets, x64 and x86; the WPF Demo is x64 only. Both official calling forms share capabilities but their declared names and return types must be preserved independently.

Sources are the [official API documentation](https://www.tampermonkey.net/documentation.php?locale=en), checked on 2026-11-06, and versioned experiments under `scripts/compatibility/evidence/`. Online documentation is not a frozen Tampermonkey release specification. `pending` means evidence is still required; existing MonkeySharp tests cannot supply it.

## API inventory

The names in each row are separate declaration spellings. Legacy globals require their exact name. `GM_info` and `GM.info` are implicit in the pinned runtime. Missing grants and `none` retain separate exposure fixtures; both expose an info-only GM object. Modern storage, resources, menus and tab-data operations are authorized by family; legacy globals retain exact declaration names. These grant rules are backed by the committed Tampermonkey 5.5.1 probes. Resource URL spellings are intentionally different. No `GM.getResourceURL`, `GM.windowClose`, Cookie listener, or webRequest removal handle is a TM target.

| Names / grants | Inputs / overloads | Immediate result (legacy; modern) | Callback / settlement | Backend / conditions |
| --- | --- | --- | --- | --- |
| `GM_info`, `GM.info` | property | metadata snapshot; snapshot | none | Actual manager, installation and execution environment; [info](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_info) |
| `GM_log`, `GM.log` | console arguments | void; Promise<void> | none | Renderer console; preserve objects |
| `GM_getValue`, `GM.getValue` | key, default | value; Promise | value / rejection | Value store and prepared mirror |
| `GM_setValue`, `GM.setValue` | key, value | void; Promise | void / rejection | Value store; preserve typed undefined |
| `GM_deleteValue`, `GM.deleteValue` | key | void; Promise | void / rejection | Value store |
| `GM_listValues`, `GM.listValues` | none | keys; Promise | keys / rejection | Value store |
| `GM_getValues`, `GM.getValues` | keys, defaults, or omitted | object; Promise | object / rejection | Value store; TM 5.3+ |
| `GM_setValues`, `GM.setValues` | object | void; Promise | void / rejection | Value store; TM 5.3+ |
| `GM_deleteValues`, `GM.deleteValues` | keys | void; Promise | void / rejection | Value store; TM 5.3+ |
| `GM_addValueChangeListener`, `GM.addValueChangeListener` | key, listener | ID; Promise | key, old, new, remote; modern resolves ID | Value store / execution ownership |
| `GM_removeValueChangeListener`, `GM.removeValueChangeListener` | ID | void; Promise | void / rejection | Value store |
| `GM_getResourceText`, `GM.getResourceText` | declared name | text; Promise | text / rejection | Prepared resources |
| `GM_getResourceURL`, `GM.getResourceUrl` | declared name | URL; Promise | URL / rejection | Prepared resources; valid data URL |
| `GM_addStyle`, `GM.addStyle` | CSS | node; Promise<node> | preserve live node | Renderer; CSP effect separately verified |
| `GM_addElement`, `GM.addElement` | tag, attributes; parent, tag, attributes | node/null; Promise<node/null> | DOM failure returns/fulfills null; normalization errors throw/reject | Renderer; CSP effect separately verified |
| `GM_xmlhttpRequest`, `GM.xmlHttpRequest` | details | abort handle; abortable Promise | ordered lifecycle, final response / error | CEF network / actual RequestContext; [XHR](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_xmlhttpRequest) |
| `GM_download`, `GM.download` | details; URL, name | abort handle; abortable Promise | progress; committed-file details / error | CEF network + host saving; anonymous TM 5.5+, Blob/File TM 5.4.6226+; [download](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_download) |
| `GM_notification`, `GM.notification` | details, ondone; text, title, image, onclick | void; Promise | click/default prevention, done; boolean clicked result (false on timeout verified) | Notification service; tag/url TM 5.0+; [notification](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_notification) |
| `GM_registerMenuCommand`, `GM.registerMenuCommand` | name, callback, accessKey/options | ID; Promise<ID> | mouse/keyboard event | Menu service; options 4.20+, id/title 5.0+, clickedFrame 5.6.6242+; [menu](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_registerMenuCommand) |
| `GM_unregisterMenuCommand`, `GM.unregisterMenuCommand` | ID | void; Promise<void> | never public boolean | Menu service |
| `GM_setClipboard`, `GM.setClipboard` | data, text/html/info, callback | void; Promise | after actual write; void / error | STA clipboard service; [clipboard](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_setClipboard) |
| `GM_openInTab`, `GM.openInTab` | URL, options/background boolean | close/onclose/closed handle; Promise<handle> | close once | Host tabs; [openInTab](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_openInTab) |
| `GM_getTab`, `GM.getTab` | legacy callback | void; Promise | script-owned tab data | Stable host tab identity |
| `GM_saveTab`, `GM.saveTab` | object, callback | void; Promise | saved / error | Stable host tab identity |
| `GM_getTabs`, `GM.getTabs` | legacy callback | void; Promise | saved tab data by ID | Stable host tab identity |
| `GM_cookie.list`, `GM.cookie.list` | optional URL/name/domain/path filters | void; Promise | cookies, error; cookie array / rejection | Authorized actual RequestContext; [Cookie](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_cookie) |
| `GM_cookie.set`, `GM.cookie.set` | cookie details | void; Promise | error only; void / rejection | Engine-accepted write; partition CRUD on 121/151, unsupported on84; request partition selection remains unavailable |
| `GM_cookie.delete`, `GM.cookie.delete` | URL/name | void; Promise | error only; void / rejection | Exact selected Cookie deletion; no public boolean |
| `GM_webRequest`, `GM.webRequest` | rules, listener | void; void (MV2 differential pending) | action, ok/error, details | Historical Chromium/MV2 only; separate environment; [webRequest](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_webRequest) |
| `unsafeWindow` | property grant | page window | none | Actual execution world |
| `window.close`, `window.focus` | exact grant | void | none | Host window policy |
| `window.onurlchange` | exact grant | nullable capability | URL event | Same-document navigation |
| `GM_audio.*`, `GM.audio.*` | mute/state/listener | callback; Promise | actual audio state | Mute implemented; audible omitted. Pinned 5.5.1 setMute resolves {isMuted}; no invented minimum TM version |

## Evidence and execution

The [pinned reference observations](#pinned-reference-observations) distinguish fixed-manager behavior from additional MonkeySharp guarantees. Topic references own current capability limits; the [evidence index](../../scripts/compatibility/evidence/README.md) groups native, reference and delivery records.

Run the same original [return probe](../../scripts/compatibility/fixtures/returns.user.js) in each manager. Its ordered log preserves immediate return shapes, microtasks, Promise settlement, DOM effects and tab closure. Separate grant installations cover missing/none, each spelling, mixed, unknown and info-only. Install only on the local test page; the probe posts JSON to `/evidence`. Interactive notification/menu effects require their own recorded checks.

The original modern reference used Edge 156.0.4314.58, Tampermonkey 5.5.1 and MV3; later fixed-stable observations use Edge 157.0.4341.42 with the same manager. The extension requires Chromium 120, independently of MonkeySharp's 84 baseline. The [original return record](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) confirms modern Promise returns for DOM, menus and openInTab, and notification timeout resolving false. It does not establish every click/tag-replacement result. Delayed tab closure passed after an immediate-close observation needed a retry; MonkeySharp's queued early-close guarantee is documented separately.

The historical MV2 reference remains unavailable. Immediate webRequest void returns and first-match overlap behavior are implementation decisions awaiting that comparison, not established differential parity. Do not install the pinned modern extension on Chromium 84 or count MV3 webRequest absence as a MonkeySharp failure.

The [reference schema](../../scripts/compatibility/evidence/schema.json) records fixture SHA-256, source, versions, settings, commit, capabilities, immediate returns, ordered events, settlements, effects and status. Raw logs stay under ignored `artifacts/`; concise [evidence records](../../scripts/compatibility/evidence/) remain in Git. Native matrices and delivery summaries have their own shapes. Only random IDs, absolute timestamps, paths and visual appearance may be normalized; preserve observable ordering and failed assertions. `blocked` is not a passing status.

## Pinned reference observations

| Topic | Tampermonkey 5.5.1/MV3 observation | MonkeySharp boundary |
| --- | --- | --- |
| DOM helpers | Invalid/empty tags, invalid attributes and invalid parents return null; modern fulfills null. Non-string tags fail normalization. Parent fallback is head, body, then document element. [DOM reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-p0-dom.json) | DOM creation/attribute/insertion failures return null; normalization and programming errors stay visible. Returned nodes do not establish CSP effects. |
| Explicit request Cookie | Header priority, duplicate names and anonymous requests were observed. [Cookie reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-p0-cookies.json) | The default native backend rejects explicit Cookie inputs; [Cookies](cookies.md#explicit-request-cookies) records the merge observations for custom backends. |
| URL changes | Payload is a plain object containing only url; once works and AbortSignal is ignored. [URL reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-p0-urlchange.json) | oldURL, property handlers, facade receivers and AbortSignal are additional guarantees; see [window extensions](../usage-guide.md#window-extensions). |
| Notifications | Replacement times out with false; the first tagged Promise can remain pending. [Notification reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-p0-notification.json) | Fixed-reference native click/default-action/navigation comparisons remain incomplete; see [host interactions](host-interactions.md#notifications). |
| Downloads | The fresh native-mode profile requires browser interaction and produces no terminal result within the probe. [Blocked reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-p0-download-errors.json) | No error outcome is inferred from missing events; [Downloads](downloads.md#error-causes) defines actual host errors. |

Fixed beta 5.6.6242 is pinned separately from stable. Its non-fetch incremental text and stream-input observations, including intentional fetch/reader/replay differences, are maintained in [HTTP requests and streams](network.md). Historical MV2 webRequest comparisons remain a separate evidence gap.

## Reproduction

Run `node scripts/compatibility/Run-Reference.cjs <Edge-exe> <extension-dir> [output.json] [fixture.user.js]` from the repository root. It requires Playwright (`PLAYWRIGHT_MODULE` may identify an existing module), checks the extension pin, serves loopback fixtures and imports through Tampermonkey's installation UI in a fresh profile. It does not copy user extension settings or installed scripts.

The default fixture and companion content live under `scripts/compatibility/fixtures/`. See the [tooling reference](../../scripts/compatibility/README.md) for arguments and prerequisites and [validation](validation.md) for native gates and the dated delivery snapshot.

## Contract interpretation

The bridge uses protocol `2`. ApiCatalog.json is shared by host metadata normalization and the composed renderer. Declared spelling, internal authorization and exposed names are independent; source directives choose strictness. The none receiver follows the [raw receiver reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-raw-receiver.json).

The [storage-authority probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-storage-authority.json) records modern storage-family authorization and exact legacy global exposure. GM.info reports actual manager, installation and execution facts, and capability reasons distinguish missing services, host policy and engine limits. Unknown host facts are omitted rather than replaced with placeholders.

## Real-script corpus intake

The original probes are not third-party compatibility evidence. The [corpus manifest](../../scripts/compatibility/corpus.json) pins unmodified third-party source and licenses for seven executable categories and preserves optional historical local-script provenance. Intake ready status is separate from actual pass/limit/block/failure records. Classify failures as syntax, GM contract, matching, authorization or world/CSP. See [corpus scope](../../scripts/compatibility/corpus/README.md) and [current evidence gaps](README.md#known-evidence-gaps).

The [network scope](network.md) includes incremental non-fetch text and staged ReadableStream input. [Menu scope](host-interactions.md#menus) includes native event fields and clickedFrame filtering within its documented frame limits. These enhancements do not close missing stable-reference interactions.
