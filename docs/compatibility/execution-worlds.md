# Execution worlds and lifecycle evidence

The adapter now executes `@sandbox DOM` / `@inject-into content` in per-installation, per-revision isolated worlds. Native CDP frame/loader/context events establish ownership. Source and delivery use a remote global-object ID tied to a context generation. Binding packets must belong to that native context's script and document before reaching the normal grant/permission gateway; the page binding rejects isolated proofs. CEF frame IDs are not cast into CDP IDs.

Raw/default and Chromium JavaScript access remain in the page world. Host-backed raw privileges require `TrustedPageWorld`; DOM dispatch uses verified native context ownership. DOM plus unsafeWindow, unsupported world requirements and unwrap reject before user source. Main-frame native manual selection can dispatch either world. CEF's additional context callbacks caused by isolated V8 worlds are coalesced on the explicitly trusted raw path; raw scripts execute once, and isolated authentication does not use the page-world deduplication marker.

The fixed Tampermonkey 5.5.1 references show private window-event replay: [document-end](../../scripts/compatibility/evidence/tampermonkey-5.5.1-lifecycle-end.json) receives DOMContentLoaded/load after registration, while [idle](../../scripts/compatibility/evidence/tampermonkey-5.5.1-lifecycle-idle.json) receives load. Replayed events are synthetic (`isTrusted: false`), and direct document listeners receive no private replay. The same original fixture bodies pass the JS facade tests. Raw and isolated browser fixtures delay the host snapshot while the DOM completes, then verify cached DOMContentLoaded delivery. Idle runs after DOMContentLoaded while a slow image still prevents load.

The [browser matrix](../../scripts/compatibility/evidence/execution-worlds-p1.json) separately records source, DOM and style CSP effects. On the tested CSP fixture, isolated source and DOM insertion succeed, inserted styles remain blocked, page inline script remains blocked and raw Function-based source execution is blocked. The adapter never calls a whole-page CSP bypass. A node being returned does not prove its style applied.

Validated combinations include full synchronous value/resource readiness, actual XHR, separate A/B globals, an invisible page canary, native proof-scope rejection, same-process child frames, explicit about:blank/srcdoc matching, SPA URL events and navigation revocation. Raw control scripts execute once alongside isolated worlds. Context-created/released counts balance in the real gate.

## Trust boundary

The browser process, host application and other CDP owners are trusted. DOM isolation supplies per-script renderer globals and context-authenticated dispatch; it is not an operating-system sandbox. Raw/default/JavaScript privileges remain subject to the host's page-world trust policy. Page-world code shares the page environment, so a token or raw deduplication marker is not a boundary against a hostile page. Isolated authentication never relies on that marker.

## Protocol session and preparation

The adapter prepares one shared CDP session per browser attachment. Start from an `about:blank` control, call `PrepareAsync()` before native initialization completes and use `NavigateAsync(url)` for business navigation. Preparation cannot postpone a URL already supplied to the control constructor. `IsLateAttach` and `IsLatePreparation` identify separate late boundaries; neither retroactively supplies missing start hooks.

Commands run on CEF's UI thread with CEF-allocated IDs so other observers can coexist. On 84, a method result may arrive synchronously inside `ExecuteDevToolsMethod` before the assigned ID returns; the session retains results only during that submission window. Cancellation/timeout removes pending ownership and ignores late results. Detach or agent closure fails pending work and releases only this observer, leaving shared domains and other DevTools owners intact.

The real protocol probe verifies Cookie readback and HTTP effects, pre-inline page injection without full GM, separate named worlds, context-ID bindings, revocation, concurrent observers, timeout and cancellation. Chromium 84 lacks future-world `executionContextName` binding. Successful registration is not evidence that a complete GM userscript started before the first page inline. The production path adds prepared data and generation-pinned delivery; it retains the timing limits below. The probe itself is not the production sandbox.

The [84](../../scripts/compatibility/evidence/cdp-84.4.10.json), [121](../../scripts/compatibility/evidence/cdp-121.3.70.json) and [151](../../scripts/compatibility/evidence/cdp-151.3.240.json) records separate protocol success from blocked strict-GM timing. Reproduce through SmokeHost `Run-E2E.ps1`, its independent `Run-CdpProbe.ps1`, or the [baseline gate](validation.md#reproduction). Reports include adjacent `cdp-probe.jsonl` files. A renderer subprocess remains a separate deployment/ABI decision if future strict timing requirements justify it.

## Remaining limits

- Full-GM document-start remains best effort, including prepared navigation. `RequireGuaranteedDocumentStart` rejects these combinations; protocol registration is not proof of pre-inline user execution.
- OOPIF child-target routing is not implemented. Failed native world creation reports an error and uses no raw fallback.
- BFCache cached executions revoke their GM/window state and require a fresh navigation; transparent restoration is not claimed.
- Isolated child-frame manual menus are not mapped to native CEF menu frames. Main-frame selection is supported.
- Historical page-frame webRequest interception is unavailable from isolated contexts. See the [historical webRequest scope](README.md#historical-webrequest).
- User-managed DOM listeners/timers are not an unload API for arbitrary code in a live document; document navigation destroys normal contexts, and runtime-owned listeners/bridge state are revoked explicitly.
