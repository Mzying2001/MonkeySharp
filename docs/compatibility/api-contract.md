# Tampermonkey contract baseline

This is the target contract, not a claim that every row already passes. The
implementation baseline is `536126c` (2026-11-06). The minimum engine remains
CefSharp 84.4.10, with 121.3.70 and 151.3.240 regression targets, x64 and x86;
the WPF Demo is x64 only. Both official calling forms share capabilities but
their declared names and return types must be preserved independently.

Sources are the [official API documentation](https://www.tampermonkey.net/documentation.php?locale=en),
checked on 2026-11-06, and versioned experiments under `evidence/`. Online
documentation is not a frozen Tampermonkey release specification. `pending`
means evidence is still required; existing MonkeySharp tests cannot supply it.

## API inventory

Each entry requires its exact `@grant` spelling (the two names in a row are
separate grants). `GM_info` is implicit. Missing grants and `none` have separate
exposure fixtures; `none` supplies `GM_info` without the privileged `GM` object.
Resource URL spellings are intentionally different. No `GM.getResourceURL`,
`GM.windowClose`, Cookie listener, or webRequest removal handle is a TM target.

| Names / grants | Inputs / overloads | Immediate result (legacy; modern) | Callback / settlement | Backend / conditions |
| --- | --- | --- | --- | --- |
| `GM_info`, `GM.info` | property | metadata snapshot; snapshot | none | Actual manager, installation and execution environment; [info](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_info) |
| `GM_log`, `GM.log` | console arguments | void; pending probe | none | Renderer console; preserve objects |
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
| `GM_addStyle`, `GM.addStyle` | CSS | node; pending probe | preserve live node | Renderer; CSP effect separately verified |
| `GM_addElement`, `GM.addElement` | tag, attributes; parent, tag, attributes | node/null; pending probe | preserve live node | Renderer; CSP effect separately verified |
| `GM_xmlhttpRequest`, `GM.xmlHttpRequest` | details | abort handle; abortable Promise | ordered lifecycle, final response / error | CEF network / actual RequestContext; [XHR](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_xmlhttpRequest) |
| `GM_download`, `GM.download` | details; URL, name | abort handle; abortable Promise | progress; committed-file details / error | CEF network + host saving; anonymous TM 5.5+, Blob/File TM 5.4.6226+ deferred; [download](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_download) |
| `GM_notification`, `GM.notification` | details, ondone; text, title, image, onclick | void; Promise | click/default prevention, done; clicked result shape pending | Notification service; tag/url TM 5.0+; [notification](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_notification) |
| `GM_registerMenuCommand`, `GM.registerMenuCommand` | name, callback, accessKey/options | ID; pending probe | mouse/keyboard event | Menu service; options 4.20+, id/title 5.0+, clickedFrame 5.6.6242+; [menu](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_registerMenuCommand) |
| `GM_unregisterMenuCommand`, `GM.unregisterMenuCommand` | ID | void; pending probe | never public boolean | Menu service |
| `GM_setClipboard`, `GM.setClipboard` | data, text/html/info, callback | void; Promise | after actual write; void / error | STA clipboard service; [clipboard](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_setClipboard) |
| `GM_openInTab`, `GM.openInTab` | URL, options/background boolean | close/onclose/closed handle; pending probe | close once | Host tabs; [openInTab](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_openInTab) |
| `GM_getTab`, `GM.getTab` | legacy callback | void; Promise | script-owned tab data | Stable host tab identity |
| `GM_saveTab`, `GM.saveTab` | object, callback | void; Promise | saved / error | Stable host tab identity |
| `GM_getTabs`, `GM.getTabs` | legacy callback | void; Promise | saved tab data by ID | Stable host tab identity |
| `GM_cookie.list`, `GM.cookie.list` | optional URL/name/domain/path filters | void; Promise | cookies, error; cookie array / rejection | Authorized actual RequestContext; [Cookie](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_cookie) |
| `GM_cookie.set`, `GM.cookie.set` | cookie details | void; Promise | error only; void / rejection | Engine-accepted write; partition unsupported on 84 |
| `GM_cookie.delete`, `GM.cookie.delete` | URL/name | void; Promise | error only; void / rejection | Exact selected Cookie deletion; no public boolean |
| `GM_webRequest`, `GM.webRequest` | rules, listener | historical result pending | info, message, details | Historical Chromium/MV2 only; separate environment; [webRequest](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_webRequest) |
| `unsafeWindow` | property grant | page window | none | Actual execution world |
| `window.close`, `window.focus` | exact grant | void | none | Host window policy |
| `window.onurlchange` | exact grant | nullable capability | URL event | Same-document navigation |
| `GM_audio.*`, `GM.audio.*` | mute/state/listener | callback; Promise | actual audio state | P2, not currently implemented; no invented minimum TM version |

## Evidence and execution

Run the same original [return probe](fixtures/returns.user.js) without changing
its body in each manager. Its ordered log preserves immediate return shapes,
microtasks, Promise settlement, DOM effects and tab closure. Install only on
the local test page; it posts JSON to `/evidence`. Grant-exposure probes must
use separate installations for missing/none, each spelling, mixed, unknown and
info-only. Interactive notification/menu actions require recorded interaction;
absence of an event is not a successful completion result.

Modern reference: installed Edge 156.0.4314.58, Tampermonkey 5.5.1, MV3,
minimum Chromium 120 in that extension's manifest. This is independent of the
MonkeySharp 84 baseline. The [5.5.1 return record](evidence/tampermonkey-5.5.1-returns.json)
confirms Promise returns for modern DOM, menu registration/unregistration and
openInTab. The notification Promise resolves to `false` after timeout in this
headless environment. Click/tag-replacement results still need interactive
evidence. The probe observes tab creation/closure through browser tab counts;
Closing after a 100 ms delay is verified. An earlier experimental run that
closed immediately left both tabs open; early-close semantics remain a
separate regression scenario, not evidence that immediate close is safe.
The historical MV2 reference is blocked: no pinned MV2 browser/extension is
installed. It remains a P2 dependency, not a passing result or an engine limitation.
Do not install this current extension on Chromium 84 or count MV3 webRequest
absence as a MonkeySharp failure.

`evidence/schema.json` defines the portable record. Keep raw logs under
`artifacts/compatibility/`; commit concise records with fixture SHA-256, source,
versions, settings, commit, capabilities, immediate returns, ordered events,
settlements, effects and status. Only random IDs, absolute timestamps, paths and
visual appearance may be normalized. Failure, engine limitation and missing
host service remain distinct. `blocked` is evidence status, not a passing test.

Run `node scripts/compatibility/Run-Reference.cjs <Edge-exe> <extension-dir>`
with Playwright installed (`PLAYWRIGHT_MODULE` can identify an existing module).
It serves a loopback page, imports the original fixture through Tampermonkey's
installation UI and verifies effects in a fresh test profile. It does not copy
user extension settings or installed scripts. Reference versions must match
the record; the extension pin is checked before execution.

The 2026-11-06 84/x64 baseline passed 237 Core, 33 adapter and 38 Demo tests,
74 JavaScript tests, SmokeHost's five compatibility cases and all 28 WPF checks.
Raw output: `artifacts/p0-step1-validation.log`. This validates existing
MonkeySharp behavior only; old assertions are not the target contract. The
first incremental build encountered mixed-version output; clean rebuilding
removed that environmental failure before the recorded gate passed.

Run [Run-Baseline.ps1](../../scripts/compatibility/Run-Baseline.ps1) for clean
84/x64 restore/build/.NET/JS/SmokeHost/Demo validation. It stops on any failure.
Other versions/platforms are explicit parameters; no cross-version parallel
builds in the same output tree. CI includes 84/x64 and 121/x64 WPF gates.

## Real-script corpus intake

The original probes are not third-party compatibility evidence. The corpus
manifest records DOM decoration, page hooks, authenticated XHR, downloads,
storage listeners, menu configuration and iframe/SPA. A corpus entry needs an
immutable upstream URL, license, version, SHA-256, unmodified source and local
fixture prerequisites before it can be marked runnable. Do not fetch mutable
latest scripts and describe them as a pinned corpus. Classify failures as
syntax, GM contract, matching, authorization or world/CSP.
