# Full-GM start and CSP decisions

Full-GM document-start and CSP effects are independent capabilities. The experiments use the production prepared-revision, engine, gateway and isolated executor, including native child-target routing. [Versioned evidence](../../scripts/compatibility/evidence/start-csp-follow-up.json) retains the assessments and their outcomes. A completed assessment does not establish a timing or CSP-bypass feature.

## Full GM before first inline

The fixture prepares `@require`, an offline resource, a synchronous value snapshot and an actual GM HTTP request. Raw and isolated user source publish separate readiness/ack/completion markers; the page's first inline captures what was visible then. Normal and private RequestContexts, main pages and genuine cross-site iframe targets are tested on 84.4.10, 121.3.70 and 151.3.240. Markers are observations only: they never establish runtime identity or authorization.

Prepared navigation still crosses asynchronous native context discovery, world creation, binding, authenticated hello and snapshot delivery before user code can run. The first inline ran before full GM in the tested combinations. Later resource/value reads and actual bridge requests succeeded. Faster typical initialization cannot remove that asynchronous dependency or create a guarantee.

The alternative tested once per combination registers an owned `debugger` statement before document code, observes the parser pause, and gives the existing production bootstrap a bounded two-second window. Readiness, request acknowledgment and completion are observed separately from the paused context and native console events. The parser paused but those full-GM markers did not become ready before resuming. The probe removes only its own script, resumes the pause and disables the debugger domain it enabled; this test-only route is not installed by production. It provides no guarantee, and a browser-wide debugger policy would also interfere with other debugger owners.

**Decision:** retain best effort and continue rejecting these combinations with `RequireGuaranteedDocumentStart`. No renderer subprocess is introduced by this work. A future renderer implementation requires a separate task and proof that the complete approved snapshot and facade are available synchronously at the native context hook before any page source. A preinjected canary or successful registration alone is insufficient. Cold profiles and the existing navigation/identity gates support the negative conclusion; this assessment does not certify arbitrary concurrent-navigation timing.

## CSP, nodes and resource effects

The server retains `default-src 'none'; script-src 'self'; style-src 'none'; connect-src 'none'; img-src 'none'`. The native HTTP bridge and isolated world are evaluated independently of page-source policy. On all tested combinations, isolated user source, the required dependency, DOM insertion, resource bytes and GM HTTP succeed. Raw Function-based user source and page/injected inline scripts remain blocked. Returned style/link nodes do not apply their requested styles. A resource data URL preserves exact JavaScript bytes but its script side effect remains blocked. The allowed same-origin external script runs, demonstrating that the test distinguishes permitted and blocked resource classes without a whole-page bypass.

OOPIF observations are read through that target's CDP session so the probe inspects the intended page world. CSP assertions check actual script/style side effects separately from returned nodes and computed width values.

**Decision:** retain the current world and CSP boundaries. No resource URL scheme or execution backend is changed. Do not advertise a returned node or decoded resource as a style/script effect. These results cover the explicit policy above and the existing native fixtures, rather than every possible CSP. Only a separately validated resource/renderer design could change this scope.

## Renderer-subprocess deployment constraints

| Build family | Native deployment that would be required |
| --- | --- |
| CefSharp 84.4.10, x64 and x86 | Two architecture-matched renderer builds and CEF/CefSharp ABI matching the host's exact distribution. |
| CefSharp 121.3.70, x64 and x86 | Two separately matched renderer builds; no binary reuse from 84. |
| CefSharp 151.3.240, x64 and x86 | Two separately matched renderer builds; no assumption of shared frame/context ABI with older releases. |

This table is a design requirement, not a tested renderer artifact. A future proposal must cover six native deliverables, package size and update coordination, subprocess startup ownership, host opt-in and custom-subprocess coexistence, normal/private request-context routing and OOPIF contexts. It must define cancellation before context readiness, revision/permission changes between preparation and navigation, complete snapshot ownership, generation-pinned dispatch, crash/restart and disposal. No capability tokens or user values should enter diagnostics. A CSP proposal separately needs a legitimate execution/resource entry point while preserving policy, bytes, lifetime and permissions. Each route gets its own minimal full-GM experiment and review before production integration.

## Reproduction

Run `./scripts/compatibility/Run-NativeProbe.ps1 -Probe start-csp -Platform x64 -CefSharpVersion 121.3.70`, with 84.4.10 or 151.3.240 for the other engines. The runner uses a fresh profile, test-only DNS and TLS policy, explicit site isolation, native event/version facts and bounded timeouts. It records two normal/private assessments and rejects a missing report or nonzero process exit. Use a new output directory for each run. See [probe parameters](../../scripts/compatibility/README.md#native-feasibility-probes) and [validation scope](validation.md#reproduction) when selecting a gate.
