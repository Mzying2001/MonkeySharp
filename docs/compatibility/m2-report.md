# Enhanced scoped delivery assessment

[中文](m2-report.zh-CN.md)

Step 36 accepts the selected enhancement scope; it does not complete every step in the plan. The [M1 evidence gaps](m1-report.md) and original step 15 remain open. Full step 30 acceptance is also incomplete. Completed feasibility assessments do not establish BFCache restoration, strict full-GM document-start or CSP bypass.

## Accepted scope

| Step | Delivered result | Remaining boundary |
| --- | --- | --- |
| 28 | Bounded incremental non-fetch text/JSON/document/xml responseText, persistent decoding and one terminal outcome | Fetch progress remains an explicit difference from fixed beta 5.6.6242; JSON/document parsing stays final-only |
| 29 | Authorized bounded ReadableStream consumption, staging before upload, cancel/navigation cleanup and redirect replay | No live renderer-source backpressure; staged replay and reader release are deliberate guarantees, not universal beta parity |
| 30 | Native input fields, registration generations, host-attested clickedFrame filtering and 84 isolated child manual menus | 121/151 child mapping and fixed-beta native click/overlap evidence remain incomplete; packaged menu fields use a supplied host callback, not native keyboard input |
| 31 | Reproducible native BFCache feasibility assessment | No genuine cache hit; restoration not implemented |
| 32 | Isolated child target/session routing, prepared values/resources, permission-checked XHR, nested targets, migration and normal/private context checks | Native OOPIF menus, strict start, BFCache and untested API scopes are separate |
| 33 | Independent full-GM start and CSP experiments plus renderer deployment decision | Strict start and CSP bypass not implemented; strict requirement rejects before source effects |
| 34 | Not selected in this delivery | No new native request-partition route; default transport still rejects cookiePartition before dispatch |
| 35 | No additional specialties selected | Audible, historical MV2 reference and other optional tasks retain their documented limits |

Steps 34 and 35 are optional P2 work selected by native evidence, corpus failures and host demand. The plan explicitly makes step 36 validate only the selected parts of steps 28–35. Moving from 33 to 36 therefore does not mean that 34/35 were implemented or merged into another step. Their status is now explicit in the progress record.

## Validation and provenance

The final matrix validates implementation `1ef4c4f` with a documentation-only patch fixing an index link label. Runtime, tests, runners, corpus and reference inputs are committed. Results are retained in `artifacts/p2-delivery/1d2373523e2f`.

| CefSharp | Platform | .NET | JavaScript | SmokeHost/CDP + normal/private OOPIF | WPF | NuGet/public consumer | Source + package corpus |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 84.4.10 | x64 | 422 passed | 129 passed | Passed | Passed | Passed | 10 + 10, declared scope |
| 84.4.10 | x86 | 373 passed | 129 passed | Passed | N/A | Passed | 10 + 10, declared scope |
| 121.3.70 | x64 | 422 passed | 129 passed | Passed | Passed | Passed | 10 + 10, declared scope |
| 121.3.70 | x86 | 373 passed | 129 passed | Passed | N/A | Passed | 10 + 10, declared scope |
| 151.3.240 | x64 | 422 passed | 129 passed | Passed | Passed | Passed | 10 + 10, declared scope |
| 151.3.240 | x86 | 373 passed | 129 passed | Passed | N/A | Passed | 10 + 10, declared scope |

Each x64 .NET total contains 305 Core, 68 adapter and 49 Demo tests; x86 excludes Demo. Every combination has 15 SmokeHost compatibility cases and normal/private OOPIF records with no runtime diagnostics. The 120 source/package corpus executions retain individual outcomes: each 84 run has six passed cases and four expected engine limitations; each 121/151 run has eight passed cases and two expected engine limitations. These limits are not functional passes.

Final matrix results and implementation identity are recorded in the [versioned delivery evidence](../../scripts/compatibility/evidence/enhanced-follow-up-delivery-p1.json). Raw logs, packages and full native probe records remain under the artifact path in that evidence. Run `./scripts/compatibility/Run-Delivery.ps1` to reproduce six isolated version/platform builds, .NET/JavaScript tests, SmokeHost/CDP, normal/private OOPIF, the three x64 WPF gates, NuGet contents, public package consumers and the unchanged pinned corpus.

The public consumer verifies exact 350003-byte staged upload, a split UTF-8 incremental prefix, callback fields and prepared GM execution inside an actual iframe target. The native OOPIF gate additionally checks two-script isolation, nested targets, migration, denied requests, private jar isolation and balanced release. None of these checks substitutes for fixed-TM native menu input or missing reference download/notification interactions.

The earlier six-combination run at `artifacts/p2-delivery/086908df9daa` passed its old assertions, but its 84/x86 normal OOPIF record retained MSC101 from a disposed CEF frame. It is superseded for final acceptance. Commit `43ffd49` protects the location probe, but the strengthened gate stopped the next matrix at 84/x86: a second retirement window remained during source dispatch. Complete exception stacks located the failure in the native IsValid getter. Commit `1ef4c4f` checks disposal first and cancels confirmed retirement before/during dispatch, while live-frame exceptions still propagate. Both regression cases fail before their respective fixes; the corrected adapter passes 68 tests. The native gate rejects unexplained MSC101/MSC231/MSC233/MSC236 initialization and dispatch errors. The final matrix uses both fixes and that corrected gate; prior failed results remain in the evidence.

The prior package-only pilot and its browser-wide CanExecute polling race remain in [pilot evidence](../../scripts/compatibility/evidence/enhanced-consumer-pilot-p1.json). Existing fixed stable/beta fixtures and reference outcomes are cited rather than relabelled as newly executed comparisons. Earlier failures, CSP restrictions, first-inline timing and Chromium 84 replaceAll limitations are preserved; expected engine limitations are not counted as passed functionality.

## Open acceptance work

Complete M1 still requires fixed-reference saved-file/ZIP/cancellation permission evidence, resolution of the reference corpus history-refresh failure, and native download/notification interaction evidence. Complete step 30 still requires the missing native frame routing and beta input comparisons. Explicit Cookie merging, request partitions, audible, historical MV2 parity, strict start and transparent BFCache retain their own unsupported or unverified status. Future work needs a concrete native route or selected specialty; this scoped delivery does not silently add a renderer subprocess or replace the network backend.
