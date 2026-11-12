# Execution worlds and lifecycle

URL listeners share execution-owned registration and cleanup with other window listeners. `once`, signal, capture identity and per-callback exception isolation apply; URL notifications remain plain objects and are never replayed from the DOMContentLoaded/load cache. See [URL guarantees and pinned differences](../usage-guide.md#window-extensions).

The adapter now executes `@sandbox DOM` / `@inject-into content` in per-installation, per-revision isolated worlds. Native CDP frame/loader/context events establish ownership. Source and delivery use a remote global-object ID tied to a context generation. Binding packets must belong to that native context's script and document before reaching the normal grant/permission gateway; the page binding rejects isolated proofs. CEF frame IDs are not cast into CDP IDs.

Raw/default and Chromium JavaScript access remain in the page world. Host-backed raw privileges require `TrustedPageWorld`; DOM dispatch uses verified native context ownership. DOM plus unsafeWindow, unsupported world requirements and unwrap reject before user source. Main-frame native manual selection can dispatch either world. CefSharp 84 also maps same-process isolated child-frame menus through a one-shot challenge sent before user source; native CEF event metadata supplies the frame. CEF's additional context callbacks caused by isolated V8 worlds are coalesced on the explicitly trusted raw path; raw scripts execute once, and isolated authentication does not use the page-world deduplication marker.

The fixed Tampermonkey 5.5.1 references show private window-event replay: [document-end](../../scripts/compatibility/evidence/tampermonkey-5.5.1-lifecycle-end.json) receives DOMContentLoaded/load after registration, while [idle](../../scripts/compatibility/evidence/tampermonkey-5.5.1-lifecycle-idle.json) receives load. Replayed events are synthetic (`isTrusted: false`), and direct document listeners receive no private replay. The same original fixture bodies pass the JS facade tests. Raw and isolated browser fixtures delay the host snapshot while the DOM completes, then verify cached DOMContentLoaded delivery. Idle runs after DOMContentLoaded while a slow image still prevents load.

The [browser matrix](../../scripts/compatibility/evidence/execution-worlds.json) separately records source, DOM and style CSP effects. On the tested CSP fixture, isolated source and DOM insertion succeed, inserted styles remain blocked, page inline script remains blocked and raw Function-based source execution is blocked. The adapter never calls a whole-page CSP bypass. A node being returned does not prove its style applied.

Validated combinations include full synchronous value/resource readiness, actual XHR, separate A/B globals, an invisible page canary, native proof-scope rejection, same-process child frames, explicit about:blank/srcdoc matching, SPA URL events and navigation revocation. Raw control scripts execute once alongside isolated worlds. Context-created/released counts balance in the real gate.

## Trust boundary

The browser process, host application and other CDP owners are trusted. DOM isolation supplies per-script renderer globals and context-authenticated dispatch; it is not an operating-system sandbox. Raw/default/JavaScript privileges remain subject to the host's page-world trust policy. Page-world code shares the page environment, so a token or raw deduplication marker is not a boundary against a hostile page. Isolated authentication never relies on that marker.

## Protocol session and preparation

The adapter prepares one shared CDP session per browser attachment. Start from an `about:blank` control, call `PrepareAsync()` before native initialization completes and use `NavigateAsync(url)` for business navigation. Preparation cannot postpone a URL already supplied to the control constructor. `IsLateAttach` and `IsLatePreparation` identify separate late boundaries; neither retroactively supplies missing start hooks.

Commands run on CEF's UI thread with CEF-allocated IDs so other observers can coexist. On 84, a method result may arrive synchronously inside `ExecuteDevToolsMethod` before the assigned ID returns; the session retains results only during that submission window. Cancellation/timeout removes pending ownership and ignores late results. Detach or agent closure fails pending work and releases only this observer, leaving shared domains and other DevTools owners intact.

The real protocol probe verifies Cookie readback and HTTP effects, pre-inline page injection without full GM, separate named worlds, context-ID bindings, revocation, concurrent observers, timeout and cancellation. Chromium 84 lacks future-world `executionContextName` binding. Successful registration is not evidence that a complete GM userscript started before the first page inline. The production path adds prepared data and generation-pinned delivery; it retains the timing limits below. The probe itself is not the production sandbox.

The [84](../../scripts/compatibility/evidence/cdp-84.4.10.json), [121](../../scripts/compatibility/evidence/cdp-121.3.70.json) and [151](../../scripts/compatibility/evidence/cdp-151.3.240.json) records separate protocol success from blocked strict-GM timing. Reproduce through SmokeHost `Run-E2E.ps1`, its independent `Run-CdpProbe.ps1`, or the [baseline gate](validation.md#reproduction). Reports include adjacent `cdp-probe.jsonl` files. A renderer subprocess remains a separate deployment/ABI decision if future strict timing requirements justify it.

## Isolated OOPIF routing

OOPIF commands and events carry their native child session. Context IDs are scoped within that session, and remote objects pin each world generation. Only iframe targets whose native parent frame belongs to this executor receive bindings and source. Discovery can briefly attach a target to inspect native ancestry, then detaches its own session if unrelated without running source. Native CEF IDs are never cast to CDP IDs.

Reserved CEF command IDs, owned-session detach and per-observer envelope filtering preserve other observers and shared domains. Read-only coexistence is tested; competing owners that mutate Target discovery/attachment state are not independently isolated DevTools clients.

The [native OOPIF matrix](../../scripts/compatibility/evidence/oopif-follow-up.json) uses distinct sites, actual iframe targets and prepared GM. It verifies source, snapshots/resources, permission-checked XHR, two script partitions, nested targets, same-process/OOPIF migration without duplicate source, balanced document release and normal/private Cookie jar isolation. [Recorded delivery](validation.md#latest-recorded-validation) covers both architectures on 84/121/151. Native menus, strict start, historical isolated webRequest and transparent BFCache retain separate limits.

Run `./scripts/compatibility/Run-NativeProbe.ps1 -Probe oopif -Platform x64 -CefSharpVersion 121.3.70`; the baseline also runs this gate for its selected engine/platform. Actual effects and runtime diagnostics are checked together. See [runner parameters](../../scripts/compatibility/README.md#native-feasibility-probes).

## Remaining limits

- Full-GM document-start remains best effort, including prepared navigation. `RequireGuaranteedDocumentStart` rejects these combinations; protocol registration is not proof of pre-inline user execution.
- Isolated OOPIF routing supports the validated source/storage/resource/XHR scope through native child sessions on 84/121/151. Native menu frame targeting remains separate; strict start and BFCache are not implied. Failed creation reports an error without raw fallback.
- BFCache cached executions revoke their GM/window state and require a fresh navigation; transparent restoration is not claimed.
- Same-process isolated child manual menus are verified on 84. On 121 the asynchronous native message event arrives with a disposed borrowed IFrame; 151 does not expose PostMessage in the tested child isolated context. Both retain main-frame selection and reject unmapped children. Page-supplied IDs and DOM markers are never used for this mapping.
- Historical page-frame webRequest interception is unavailable from isolated contexts. See the [historical webRequest scope](README.md#historical-webrequest).
- User-managed DOM listeners/timers are not an unload API for arbitrary code in a live document; document navigation destroys normal contexts, and runtime-owned listeners/bridge state are revoked explicitly.

The [native BFCache assessment](../../scripts/compatibility/evidence/bfcache-follow-up.json) includes cacheable control pages without a runtime/CDP attachment. Neither control nor prepared raw/isolated runs retained the closure or reported persisted pageshow on 84/121/151, including explicit BFCache enablement. Transparent restoration remains unimplemented; no global BFCache disablement was used. Private-context restoration and suspended-state listener/storage recovery remain unverified because the basic cache-hit prerequisite was not met. The [native probe runner](../../scripts/compatibility/Run-NativeProbe.ps1) preserves capability outcomes separately from probe completion.

The [start/CSP decisions](start-csp-decision.md) use full prepared GM and real child sessions. The owned parser-pause alternative still lacked full-GM readiness/acknowledgement/completion before resume. CSP effects and renderer deployment constraints are recorded separately; strict start and whole-page bypass remain unavailable.
