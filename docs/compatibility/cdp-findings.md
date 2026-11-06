# Browser protocol feasibility

The original `--cdp-probe` SmokeHost mode runs against the selected real CEF
engine. `Run-E2E.ps1` also runs this probe; `Run-CdpProbe.ps1` runs it independently.
Raw evidence is saved alongside the smoke results as `cdp-probe.jsonl`.

The adapter has one lazily prepared session per browser attachment. Call
`PrepareAsync()` before native initialization completes, and use
`NavigateAsync(url)` for business navigation after preparation. Initialize
the control with `about:blank`; calling preparation cannot postpone a URL
already supplied to the control constructor. `IsLateAttach` and
`IsLatePreparation` report those distinct late boundaries. Late attachment
does not retroactively execute the current document's missing start hooks.

Commands run on CEF's UI thread with CEF-allocated IDs, preserving coexistence
with other observers. CefSharp 84 can deliver method results synchronously
inside `ExecuteDevToolsMethod`, before returning the assigned ID. The session
retains results only during that submission window, then matches its ID.
Cancellation/timeout removes pending ownership; late results are ignored.
Detach or agent closure fails pending work and releases only this observer.
Neither shared protocol domains nor another owner's DevTools are closed.

## Observed scope

On 84.4.10, 121.3.70 and 151.3.240, the probe checks:

- Actual Cookie acceptance, Lax readback, HTTP credential transmission and
  deletion. This justifies using a browser-scoped shared CDP Cookie backend.
- Page preinjection before the first inline and two separately named isolated
  worlds with independent globals and a shared DOM event boundary.
- Context ID binding restricted to one world. Identity comes from native
  `Runtime.executionContextCreated` and its `auxData.frameId`, not from JS
  claiming a script/frame ID. CDP frame IDs are never cast from CEF frame IDs.
- Context revocation across navigation, concurrent independent observers,
  command timeout and cancellation.
- A real installed document-start userscript reading the actual GM value
  mirror/resource snapshot and making a real GM XHR. It eventually executes
  correctly, but the page's first inline observes it as not yet ready.

The concise version records in `evidence/cdp-*.json` separate successful
protocol checks from the blocked strict-GM timing requirement. The latter is
a current host-bootstrap limitation, not evidence that Chromium cannot ever
support it. Registration alone is not a passing full-GM timing result.

## Decision and outstanding execution work

Use CDP for the basic Cookie backend. Keep the isolated-world route as a
prototype until the P1 prepared execution package supplies complete values,
resources and context-authenticated dispatch before user source starts.
P0 experiments alone did not establish a production sandbox. P1 now adds
per-script DOM execution with prepared data and native context-authenticated
dispatch; see [execution worlds](execution-worlds.md). Guaranteed full-GM start
continues to be rejected and is not inferred from protocol registration.

The probe's context dictionary demonstrates native event ownership only. It
does not implement a production CEF-frame/CDP-frame/document-generation map,
OOPIF routing, unsafeWindow access, BFCache restoration, or a privileged
isolated-world transport. The production route now owns CDP frame/context/document identities directly and pins source/delivery to remote global-object IDs. OOPIF and transparent BFCache restoration remain explicit limits in the P1 execution record. Chromium 84 lacks
future-world `executionContextName` binding; context-ID registration is
necessarily later than context creation. Do not use that registration to
claim GM readiness before first inline.

Do not add a renderer subprocess at this stage: the protocol primitives and
no-GM timing route work. Reassess that deployment/ABI cost separately only if
the prepared P1 path still cannot cover the required strict scenarios.
