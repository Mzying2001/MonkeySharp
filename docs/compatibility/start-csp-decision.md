# Full-GM start and CSP assessment

Full-GM document-start is best effort, and page script and style effects remain subject to CSP. The native probes use the production runtime and isolated executor, including native child-target routing. See the [assessment record](../../scripts/compatibility/evidence/start-csp-follow-up.json) for the exact configurations and outcomes.

## Full GM before first inline

The fixture tests a prepared `@require`, an offline resource, a synchronous value snapshot and an actual GM HTTP request. The page's first inline script records user-script readiness, request acknowledgment and completion. The [historical run](../../scripts/compatibility/evidence/start-csp-follow-up.json) covers normal and private RequestContexts on main pages and cross-site iframe targets.

Prepared navigation still depends on asynchronous native context discovery, world creation, authenticated binding and snapshot delivery. The first inline script ran before full GM in the tested combinations, with resource/value reads and GM requests succeeding later. The parser-pause probe reached the pause but did not observe the three full-GM markers within its two-second window. The adapter rejects requests for strict timing through `RequireGuaranteedDocumentStart`.

## CSP, nodes and resource effects

The fixture serves `default-src 'none'; script-src 'self'; style-src 'none'; connect-src 'none'; img-src 'none'`. Under this policy, isolated user source, its dependency, DOM insertion, resource reads and GM HTTP succeeded. CSP blocked raw `Function` execution, page and injected inline scripts, style application and script execution from a resource data URL. The allowed same-origin external script ran. The probe checks actual script and style effects separately from returned DOM nodes and resource bytes.

## Renderer-subprocess deployment constraints

A renderer subprocess requires a native build that matches the host's CEF/CefSharp ABI and process architecture. A strict-start guarantee requires the complete approved GM facade and snapshot to be available synchronously before the first page script runs.

## Reproduction

Run `./scripts/compatibility/Run-NativeProbe.ps1 -Probe start-csp -Platform x64 -CefSharpVersion 87.1.132` and repeat with `156.0.300`. The runner uses a fresh profile, test-only DNS and TLS policy, explicit site isolation, native event/version facts and bounded timeouts. It records two normal/private assessments and rejects a missing report or nonzero process exit. Use a new output directory for each run. See [probe parameters](../../scripts/compatibility/README.md#native-feasibility-probes) and [validation scope](validation.md#reproduction) when selecting a gate.
