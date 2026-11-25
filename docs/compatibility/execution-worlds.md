# Execution worlds and lifecycle

Choose the execution world from the script's page-access needs and the host's trust policy. This topic owns the adapter's world, frame and lifecycle limits; the [userscript guide](../usage-guide.md#execution-grants-and-compatibility) owns declarations and script behavior, and the [integration reference](../integration-reference.md#attachment-and-navigation) owns attachment and navigation.

## World and frame selection

| Metadata / world | Privileged dispatch | Page-global access |
| --- | --- | --- |
| Default/raw or Chromium JavaScript / page world | Recognized grants beyond info require host `TrustedPageWorld`. | `unsafeWindow` refers to the page global except with `@grant none`. |
| `@sandbox DOM` or `@inject-into content` / isolated world | Context-authenticated transport; grants, services and host authorization still apply. | Separate script globals; no `unsafeWindow`. |

Conflicting world requirements, explicit DOM plus an `unsafeWindow` grant, and unsupported unwrap reject before source starts with `MSR215_EXECUTION_WORLD_UNAVAILABLE`; there is no page-world fallback. Neither world provides guaranteed full-GM document-start or transparent BFCache restoration.

| Isolated execution scope | Support |
| --- | --- |
| Main and same-process child source/storage/resources/XHR | Supported |
| OOPIF source/storage/resources/XHR | Supported through native child sessions |
| Main-frame `@run-at context-menu` selection | Supported |
| Same-process child `@run-at context-menu` selection | Supported after native frame and document verification |
| OOPIF `@run-at context-menu` selection | Unsupported |

This table concerns source execution. `GM.registerMenuCommand` callbacks use their host service and optional clickedFrame filtering. Page-world manual source selection uses the current native CEF frame session. See [menu scope](host-interactions.md#menus) and the [recorded engine/platform matrix](validation.md#latest-recorded-validation).

## Trust boundary

The browser process, host application and other CDP owners are trusted. DOM isolation supplies per-script renderer globals and context-authenticated dispatch; it is not an operating-system sandbox. Raw/default/JavaScript privileges remain subject to the host's page-world trust policy. Page-world code shares the page environment, so a token or raw deduplication marker is not a boundary against a hostile page. Isolated authentication never relies on that marker.

## Dispatch identity

The adapter executes `@sandbox DOM` / `@inject-into content` in per-installation, per-revision isolated worlds. Native CDP frame/loader/context events establish ownership. Source and delivery use a remote global-object ID tied to a context generation. Binding packets must belong to that native context's script and document before reaching the normal grant/permission gateway; the page binding rejects isolated proofs. CEF frame IDs are not cast into CDP IDs.

Raw/default and Chromium JavaScript access remain in the page world. Host-backed raw privileges require `TrustedPageWorld`; DOM dispatch uses verified native context ownership. DOM plus unsafeWindow, unsupported world requirements and unwrap reject before user source. Main-frame native manual selection can dispatch either world. CEF's additional context callbacks caused by isolated V8 worlds are coalesced on the explicitly trusted raw path; raw scripts execute once, and isolated authentication does not use the page-world deduplication marker.

Native CEF frame recovery and URL-probe retries are documented in [lifecycle maintenance](../development.md#native-lifecycle-maintenance).

## Lifecycle replay and CSP

The fixed Tampermonkey 5.5.1 references show private window-event replay: [document-end](../../scripts/compatibility/evidence/tampermonkey-5.5.1-lifecycle-end.json) receives DOMContentLoaded/load after registration, while [idle](../../scripts/compatibility/evidence/tampermonkey-5.5.1-lifecycle-idle.json) receives load. Replayed events are synthetic (`isTrusted: false`), and direct document listeners receive no private replay. The same original fixture bodies pass the JS facade tests. Raw and isolated browser fixtures delay the host snapshot while the DOM completes, then verify cached DOMContentLoaded delivery. Idle runs after DOMContentLoaded while a slow image still prevents load.

URL notifications remain plain objects and are never replayed from the DOMContentLoaded/load cache. Listener options, cleanup and payloads belong to the [URL API contract](../usage-guide.md#window-extensions).

The [browser matrix](../../scripts/compatibility/evidence/execution-worlds.json) separately records source, DOM and style CSP effects. On the tested CSP fixture, isolated source and DOM insertion succeed, inserted styles remain blocked, page inline script remains blocked and raw Function-based source execution is blocked. The adapter never calls a whole-page CSP bypass. A node being returned does not prove its style applied.

Validated combinations include full synchronous value/resource readiness, actual XHR, separate A/B globals, an invisible page canary, native proof-scope rejection, same-process child frames, explicit about:blank/srcdoc matching, SPA URL events and navigation revocation. Raw control scripts execute once alongside isolated worlds. Context-created/released counts balance in the real gate.

## Protocol session and preparation

The adapter prepares one shared CDP session per browser attachment. Follow the [host attachment and navigation sequence](../integration-reference.md#attachment-and-navigation) to prepare before business navigation. The preparation APIs expose readiness and late-attachment state; they do not upgrade the timing guarantee.

Commands run on CEF's UI thread with CEF-allocated IDs so other observers can coexist. A method result may arrive synchronously inside `ExecuteDevToolsMethod` before the assigned ID returns; the session retains results only during that submission window. Cancellation/timeout removes pending ownership and ignores late results. Detach or agent closure fails pending work and releases only this observer, leaving shared domains and other DevTools owners intact.

The real protocol probe verifies Cookie readback and HTTP effects, pre-inline page injection without full GM, separate named worlds, context-ID bindings, revocation, concurrent observers, timeout and cancellation. Chromium 87 lacks future-world `executionContextName` binding. Successful registration is not evidence that a complete GM userscript started before the first page inline. The production path adds prepared data and generation-pinned delivery; it retains the timing limits below. The probe itself is not the production sandbox.

The [historical protocol results](../../scripts/compatibility/evidence/README.md#capability-matrices) record protocol checks and strict GM timing separately. Reproduce through SmokeHost `Run-E2E.ps1`, its independent `Run-CdpProbe.ps1`, or the [baseline gate](validation.md#reproduction). Reports include adjacent `cdp-probe.jsonl` files.

## Isolated OOPIF routing

OOPIF commands and events carry their native child session. Context IDs are scoped within that session, and remote objects pin each world generation. Only iframe targets whose native parent frame belongs to this executor receive bindings and source. Discovery can briefly attach a target to inspect native ancestry, then detaches its own session if unrelated without running source. Native CEF IDs are never cast to CDP IDs.

Reserved CEF command IDs, owned-session detach and per-observer envelope filtering preserve other observers and shared domains. Read-only coexistence is tested; competing owners that mutate Target discovery/attachment state are not independently isolated DevTools clients.

The native x64 OOPIF probe uses distinct sites, actual iframe targets and prepared GM. It verifies source, snapshots/resources, permission-checked XHR, two script partitions, nested targets, same-process/OOPIF migration without duplicate source, balanced document release and normal/private Cookie jar isolation. See the [native OOPIF record](../../scripts/compatibility/evidence/oopif-follow-up.json) and [validation matrices](validation.md#latest-recorded-validation).

Run `./scripts/compatibility/Run-NativeProbe.ps1 -Probe oopif -Platform x64 -CefSharpVersion 87.1.132`. The baseline also runs this gate for its selected engine and platform. See [runner parameters](../../scripts/compatibility/README.md#native-feasibility-probes).

## Remaining limits

- Full-GM document-start remains best effort, including prepared navigation. `RequireGuaranteedDocumentStart` rejects these combinations; protocol registration is not proof of pre-inline user execution.
- Failure to create an isolated OOPIF context reports an error without raw fallback.
- BFCache cached executions revoke their GM/window state and require a fresh navigation; transparent restoration is not claimed.
- Isolated child menu selection requires an asynchronous mapping between the native CEF frame and the current document in the root CDP session. The adapter checks a temporary lexical marker written through the native frame. An unverified frame has no isolated menu entry, and replacing the document invalidates existing selections.
- Historical page-frame webRequest interception is unavailable from isolated contexts. See the [historical webRequest scope](README.md#historical-webrequest).
- User-managed DOM listeners/timers are not an unload API for arbitrary code in a live document; document navigation destroys normal contexts, and runtime-owned listeners/bridge state are revoked explicitly.

The [native BFCache assessment](../../scripts/compatibility/evidence/bfcache-follow-up.json) includes cacheable control pages without a runtime/CDP attachment. Neither control nor prepared raw/isolated runs retained the closure or reported persisted pageshow in the recorded runs, including explicit BFCache enablement. Transparent restoration remains unimplemented; no global BFCache disablement was used. Private-context restoration and suspended-state listener/storage recovery remain unverified because the basic cache-hit prerequisite was not met. The [native probe runner](../../scripts/compatibility/Run-NativeProbe.ps1) preserves capability outcomes separately from probe completion.

See the [start/CSP assessment](start-csp-decision.md) for measured pre-inline readiness, parser-pause results and CSP effects.
