<a id="tampermonkey-contract-baseline"></a>

# Tampermonkey reference baseline

This reference identifies the calling forms MonkeySharp targets and the evidence used to interpret them. The [userscript guide](../usage-guide.md#api-index) owns MonkeySharp parameters, returns and examples; [compatibility topics](README.md) own its supported scope and differences. The [integration reference](../integration-reference.md) owns host backends and interfaces.

Three kinds of claims must stay distinct: **official documentation** describes a changing upstream API; **pinned observations** describe the exact manager/browser recorded; **MonkeySharp guarantees** describe this implementation. A native test cannot establish upstream behavior, and a reference observation does not establish a MonkeySharp capability. The original official-document review was recorded on 2026-11-06; the [download](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_download), [XHR](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_xmlhttpRequest), [historical webRequest](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_webRequest) and [audio](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_audio) descriptions were rechecked on 2026-11-12. Neither review turns the online documentation into a frozen release specification.

## API inventory

This inventory gives MonkeySharp's selected calling-form contract, with legacy and modern results kept separate. The final column identifies the upstream basis and exactly what the fixed probes establish; an exposure check is not a return-value measurement. It does not claim that every upstream option or effect is implemented. Detailed MonkeySharp behavior belongs to the linked guides and topics.

| Legacy / modern names | Immediate legacy result | Immediate modern result | Reference basis / qualification |
| --- | --- | --- | --- |
| `GM_info` / `GM.info` | Snapshot property | Snapshot property | [Official info](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_info); both implicit in pinned grant probes. |
| `GM_log` / `GM.log` | void | Promise | [Page-helper return probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-page-helpers.json); local object handling has [MonkeySharp differences](README.md#page-and-tab-services). |
| `GM_getValue` / `GM.getValue` | value | Promise of value | [Official storage API](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_values); [authority](../../scripts/compatibility/evidence/tampermonkey-5.5.1-storage-authority.json) records exposure, while [timing](../../scripts/compatibility/evidence/tampermonkey-5.5.1-storage-timing.json) records listener/write order. |
| `GM_setValue`, `GM_deleteValue` / `GM.setValue`, `GM.deleteValue` | void | Promise | Official storage forms; host commit/replay confirmation and atomic batches are [MonkeySharp guarantees](README.md#storage-and-resources). |
| `GM_listValues` / `GM.listValues` | keys | Promise of keys | [Official storage forms](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_values); not a claim of a fixed return measurement for each method. |
| `GM_getValues` / `GM.getValues` | object | Promise of object | [Official bulk storage](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_values), TM 5.3+; authority probes establish family exposure. |
| `GM_setValues`, `GM_deleteValues` / `GM.setValues`, `GM.deleteValues` | void | Promise | [Official bulk storage](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_values), TM 5.3+; authority probes establish family exposure. |
| `GM_addValueChangeListener` / `GM.addValueChangeListener` | Listener ID | Promise of ID | [Official listener returns](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_values); pinned storage timing additionally records synchronous local delivery. |
| `GM_removeValueChangeListener` / `GM.removeValueChangeListener` | void | Promise | [Official storage forms](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_values); not a claim of a fixed return measurement for each method. |
| `GM_getResourceText` / `GM.getResourceText` | text | Promise of text | [Official resource forms](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_getResource); [prepared-content reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-prepared-content.json) measures legacy text effects, not the modern return. Decoding and preparation guarantees have [separate scope](README.md#storage-and-resources). |
| `GM_getResourceURL` / `GM.getResourceUrl` | URL | Promise of URL | [Official resource URL](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_getResource); MonkeySharp's [data-URL contract](../usage-guide.md#resources) is explicit. |
| `GM_addStyle`, `GM_addElement` / `GM.addStyle`, `GM.addElement` | node; element may be null | Promise of corresponding result | [Return](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) and [DOM](../../scripts/compatibility/evidence/tampermonkey-5.5.1-dom.json) probes; nodes alone do not prove CSP effects. |
| `GM_xmlhttpRequest` / `GM.xmlHttpRequest` | Abort handle | Abortable Promise of response | [Official XHR](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_xmlhttpRequest); stable and beta behavior are separated in [network scope](network.md). |
| `GM_download` / `GM.download` | Abort handle | Abortable Promise of download details | [Official download](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_download): Blob/File 5.4.6226+, anonymous 5.5+. MonkeySharp's file-commit boundary and blocked native reference are in [downloads](downloads.md). |
| `GM_notification` / `GM.notification` | void | Promise | [Return probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) verifies timeout resolves false; click/default-action comparisons remain [incomplete](host-interactions.md#notifications). |
| `GM_registerMenuCommand` / `GM.registerMenuCommand` | Command ID | Promise of ID | [Official menu](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_registerMenuCommand): options 4.20+, id/title 5.0+, clickedFrame 5.6.6242+; native click comparisons have [separate limits](host-interactions.md#menus). |
| `GM_unregisterMenuCommand` / `GM.unregisterMenuCommand` | void | Promise of void | [Return probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json); no public removal boolean. |
| `GM_setClipboard` / `GM.setClipboard` | void | Promise | [Official clipboard](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_setClipboard) describes callback/await forms; [family probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-group-clipboard.json) measures exposure, not write completion or returns. |
| `GM_openInTab` / `GM.openInTab` | close/onclose/closed handle | Promise of handle | [Return probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) measures immediate returns and the fulfilled handle; [page-helper reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-page-helpers.json) adds closure effects. Queued early closure is a separate MonkeySharp guarantee. |
| `GM_getTab`, `GM_getTabs` / `GM.getTab`, `GM.getTabs` | void; callback receives data | Promise of data | [Official tab-data forms](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_tabs); [family probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-group-tabs.json) measures exposure, not returned data or settlement. |
| `GM_saveTab` / `GM.saveTab` | void | Promise | Same official tab-data forms; fixed family exposure does not measure commit completion. |
| `GM_cookie.list` / `GM.cookie.list` | void; callback receives cookies/error | Promise of cookies | [Official Cookie callbacks](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_cookie); [pinned returns](../../scripts/compatibility/evidence/tampermonkey-5.5.1-cookie-returns.json) measure immediate forms and modern settlement, without invoking legacy callbacks. |
| `GM_cookie.set`, `GM_cookie.delete` / `GM.cookie.set`, `GM.cookie.delete` | void; callback receives error | Promise of void | Same official callbacks and pinned return/settlement scope; reference partition side effects and native engine support are distinguished in [cookies](cookies.md). |
| `GM_webRequest` / `GM.webRequest` | void in MonkeySharp | void in MonkeySharp | [Official historical API](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_webRequest); MV2 differential unavailable, so immediate returns and overlap behavior are implementation choices. |
| `unsafeWindow` | Page-window property | Same property name | [Official unsafeWindow](https://www.tampermonkey.net/documentation.php?locale=en&q=unsafeWindow); actual access depends on the execution world. |
| `window.close`, `window.focus` | void | Same names | Official exact grants; MonkeySharp delegates to host window policy. |
| `window.onurlchange` | Capability/property | Same name | [Pinned URL reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-urlchange.json); additional MonkeySharp guarantees are listed below. |
| `GM_audio.*` / `GM.audio.*` | void; callbacks | Promise | [Pinned 5.5.1 audio](../../scripts/compatibility/evidence/tampermonkey-5.5.1-audio.json) resolves setMute with `{isMuted}`, while official documentation describes void; [native audio support](README.md#tab-audio) is separate. |

Legacy globals require exact declaration names. Modern storage, resources, menus and tab-data operations are authorized by family in the pinned 5.5.1 probes. Missing grants and `none` retain separate exposure fixtures; both expose info. Resource URL spellings intentionally differ. The selected baseline does not introduce `GM.getResourceURL`, `GM.windowClose`, Cookie listeners or a webRequest removal handle. See the [grant test baselines](../../scripts/compatibility/evidence/README.md#test-reference-baselines) and [modern-family observations](../../scripts/compatibility/evidence/README.md#fixed-reference-observations).

## Evidence and execution

The [pinned reference observations](#pinned-reference-observations) distinguish fixed-manager behavior from additional MonkeySharp guarantees. Topic references own current capability limits; the [evidence index](../../scripts/compatibility/evidence/README.md) groups native, reference and delivery records.

Run the same original [return probe](../../scripts/compatibility/fixtures/returns.user.js) in each manager. Its ordered log preserves immediate return shapes, microtasks, Promise settlement, DOM effects and tab closure. Separate grant installations cover missing/none, each spelling, mixed, unknown and info-only. Install only on the local test page; the probe posts JSON to `/evidence`. Interactive notification/menu effects require their own recorded checks.

The original modern reference used Edge 156.0.4314.58, Tampermonkey 5.5.1 and MV3; later fixed-stable observations use Edge 157.0.4341.42 with the same manager. The extension requires Chromium 120, independently of MonkeySharp's 84 baseline. The [original return record](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) confirms modern Promise returns for DOM, menus and openInTab, and notification timeout resolving false. It does not establish every click/tag-replacement result. Delayed tab closure passed after an immediate-close observation needed a retry; MonkeySharp's queued early-close guarantee is documented separately.

The historical MV2 reference remains unavailable. Immediate webRequest void returns and first-match overlap behavior are implementation decisions awaiting that comparison, not established differential parity. Do not install the pinned modern extension on Chromium 84 or count MV3 webRequest absence as a MonkeySharp failure.

The [reference schema](../../scripts/compatibility/evidence/schema.json) records fixture SHA-256, source, versions, settings, commit, capabilities, immediate returns, ordered events, settlements, effects and status. Raw logs stay under ignored `artifacts/`; concise [evidence records](../../scripts/compatibility/evidence/) remain in Git. Native matrices and delivery summaries have their own shapes. Only random IDs, absolute timestamps, paths and visual appearance may be normalized; preserve observable ordering and failed assertions. `blocked` is not a passing status.

## Pinned reference observations

| Topic | Tampermonkey 5.5.1/MV3 observation | MonkeySharp boundary |
| --- | --- | --- |
| DOM helpers | Invalid/empty tags, invalid attributes and invalid parents return null; modern fulfills null. Non-string tags fail normalization. Parent fallback is head, body, then document element. [DOM reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-dom.json) | DOM creation/attribute/insertion failures return null; normalization and programming errors stay visible. Returned nodes do not establish CSP effects. |
| Explicit request Cookie | Header priority, duplicate names and anonymous requests were observed. [Cookie reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-cookies.json) | The default native backend rejects explicit Cookie inputs; [Cookies](cookies.md#explicit-request-cookies) records the merge observations for custom backends. |
| URL changes | Payload is a plain object containing only url; once works and AbortSignal is ignored. [URL reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-urlchange.json) | oldURL, property handlers, facade receivers and AbortSignal are additional guarantees; see [window extensions](../usage-guide.md#window-extensions). |
| Notifications | Replacement times out with false; the first tagged Promise can remain pending. [Notification reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-notification.json) | Fixed-reference native click/default-action/navigation comparisons remain incomplete; see [host interactions](host-interactions.md#notifications). |
| Downloads | The fresh native-mode profile requires browser interaction and produces no terminal result within the probe. [Blocked reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-download-errors.json) | No error outcome is inferred from missing events; [Downloads](downloads.md#error-causes) defines actual host errors. |

Fixed beta 5.6.6242 is pinned separately from stable. Its non-fetch incremental text and stream-input observations, including intentional fetch/reader/replay differences, are maintained in [HTTP requests and streams](network.md). Historical MV2 webRequest comparisons remain a separate evidence gap.

## Reproduction

Use the [reference runner instructions](../../scripts/compatibility/README.md#pinned-reference) for prerequisites, arguments and the environment pin. The runner serves loopback fixtures and imports through Tampermonkey's installation UI in a fresh profile; it does not copy user settings or scripts.

The default fixture and companion content live under `scripts/compatibility/fixtures/`. See the [tooling reference](../../scripts/compatibility/README.md) for arguments and prerequisites and [validation](validation.md) for native gates and the dated delivery snapshot.

## Contract interpretation

In MonkeySharp, [ApiCatalog.json](../../Mzying2001.MonkeySharp.Core/Js/ApiCatalog.json) is shared by host metadata normalization and the renderer. Declared spelling, internal authorization and exposed names are independent; source directives choose strictness. The none receiver follows the [raw receiver reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-raw-receiver.json). Host protocol requirements belong to the [integration reference](../integration-reference.md#bridge-protocol).

The [storage-authority probe](../../scripts/compatibility/evidence/tampermonkey-5.5.1-storage-authority.json) records modern storage-family authorization and exact legacy global exposure. GM.info reports actual manager, installation and execution facts, and capability reasons distinguish missing services, host policy and engine limits. Unknown host facts are omitted rather than replaced with placeholders.

## Real-script corpus intake

The original probes are not third-party compatibility evidence. The [corpus manifest](../../scripts/compatibility/corpus.json) pins unmodified third-party source and licenses for seven executable categories and preserves optional historical local-script provenance. Intake ready status is separate from actual pass/limit/block/failure records. Classify failures as syntax, GM contract, matching, authorization or world/CSP. See [corpus scope](../../scripts/compatibility/corpus/README.md) and [current evidence gaps](README.md#known-evidence-gaps).

The [network scope](network.md) includes incremental non-fetch text and staged ReadableStream input. [Menu scope](host-interactions.md#menus) includes native event fields and clickedFrame filtering within its documented frame limits. These enhancements do not close missing stable-reference interactions.
