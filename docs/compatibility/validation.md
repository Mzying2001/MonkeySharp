# Compatibility validation

Native MonkeySharp checks, pinned Tampermonkey comparisons and third-party script intake answer different questions. A passed bootstrap or mock test does not establish browser timing, HTTP credentials, file completion or WPF effects. Missing evidence is not a pass. See [current capabilities and gaps](README.md).

## Browser-independent checks

Core/JavaScript tests use the tracked fixtures and evidence and require no CefSharp browser, local notes or local corpus source. Use a .NET SDK capable of building the net8.0 test project and Node.js 24:

```powershell
dotnet test Mzying2001.MonkeySharp.Core.Tests/Mzying2001.MonkeySharp.Core.Tests.csproj -c Release
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

## Reproduction

Run from the repository root in a Windows desktop session. Use an SDK that supports the solution's `.slnx` format and Node.js 24, the CI-tested JavaScript version. Real-CefSharp builds require an explicit x64/x86 platform and matching managed/native versions. The Demo gate runs only for x64.

```powershell
# Clean solution/.NET/JS/SmokeHost/CDP, plus WPF on x64.
./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10

# Six isolated native combinations, packaging and public-API consumers.
./scripts/compatibility/Run-Delivery.ps1

# Optional: additionally install an existing local script without source edits.
./scripts/compatibility/Run-Delivery.ps1 -CorpusScript C:/path/to/script.user.js
```

The baseline accepts 84.4.10, 121.3.70 or 151.3.240 and x64/x86. Do not build different native versions concurrently in the same output tree. The delivery runner creates independent short-path worktrees, assets, profiles and unique package versions. It records the base commit and source-patch SHA-256, stops on failure and preserves failed worktrees. Successful worktrees are removed after validating their resolved paths. Reports and packages remain under `artifacts/p2-delivery/<run>/`. `-KeepWorktrees` preserves successful worktrees too; `-PackageOnly` skips the baseline and is diagnostic only.

The package consumer uses NuGet packages rather than project references. It checks actual CEF version/platform, Core AnyCPU, public services, package contents and optional local-script effects. An omitted `-CorpusScript` does not require local notes or an uncommitted script. See [tool parameters and prerequisites](../../scripts/compatibility/README.md) and the [integration build reference](../integration-reference.md#build-package-and-verify).

## SmokeHost cases and results

The SmokeHost embeds repository-owned `.user.js` fixtures and installs them through the normal repository. Two ephemeral loopback HTTP servers supply runtime inputs. These cases validate MonkeySharp contracts; they are separate from third-party corpus and Tampermonkey differential runs.

| Result ID | Required evidence |
| --- | --- |
| `prepared` | Offline prepared revisions, dependency scope/charset/SRI, complete resource/storage snapshots and first-statement reads. |
| `conditions` | Prepared URL rules, normal/private run-in and manually selected context-menu effects. |
| `isolated` | Separate per-script globals, authenticated dispatch, synchronous readiness, child frames and navigation cleanup. |
| `replay` | Private window lifecycle replay and document-idle before slow load. |
| `csp` | Separate source, DOM and style effects without whole-page bypass. |
| `permissions` | Coalescing, allow-once, remembered denial, redirect checks and transfer timeout pausing. |
| `binary` | Exact Blob/File/form bytes, charset boundaries, document parsing, streams and binary downloads. |
| `pageHelpers` | Real DOM/style, local logging, tab handles and window/helper behavior. |
| `audio` | Native mute readback, listeners, frames/navigation and explicit audible unavailability. |
| `partitions` | Version-specific partition CRUD/readback/deletion and independent request-partition limitations. |
| `frames` | Single-key and bulk GM storage calls, including all six modern/legacy bulk entry points, typed undefined, listener return contracts, cross-instance notifications and navigation snapshots, in the main frame, a same-origin child and a child on another port; once-per-document execution, `@noframes`, child navigation and context release. |
| `spaUrl` | `pushState`, `replaceState`, hash navigation and history back; both URL callback forms, old/new URLs, host lifecycle events, grant gating and no reinjection. |
| `download` | Modern Promise/handle and legacy callbacks; actual HTTP downloads with exact file bytes, cancellation after progress, timeout, HTTP failure, one terminal callback and partial-file cleanup. |
| `cookie` | Verified CRUD, SameSite, HTTP/TLS cookie effects, exact multi-path deletion, and two real RequestContexts with isolated network state. |
| `webRequest` | Browser-native fetch cancellation and redirect, callback details, server request counts, explicit removal and navigation cleanup. |

The download service is test-only: Chromium executes GM calls and the supplied host service transfers local HTTP bytes to an isolated temporary directory. It does not test Chromium's download UI. Existing storage/XHR/notification/tab/navigation checks remain required.

Each case emits a `compatibility-case` JSON record containing `caseId`, `success`, named `checks`, and failure context (`frameId`, `documentId`, `url`, `state`, `error`). `smoke-summary.compatibility` must contain all 15 successful results. The runner independently rejects missing/duplicate cases, failed assertions, wrong process architecture or CefSharp assembly version, missing detach/disposal and nonzero exits. Polling has a 15-second deadline; the host has a 90-second watchdog and the runner kills a stalled process tree after 120 seconds.

The `frames` child-navigation check accepts either `ContextReleased` or `NavigationSuperseded` for the old document ID. Both transitions retire the document in the runtime; native callback order can make Chromium 84 report supersession first. A terminal event for another document on the same native frame does not satisfy this check. The replacement must still have a new document ID, run once and preserve the required storage snapshot.

The adapter's initial `location.href` probe and the legacy fixture result reader retry canceled read-only evaluations within a shared 15-second budget per initialization/fixture phase. Each attempt receives at most two seconds, followed by a 50 ms retry delay capped by the remaining budget. CefSharp represents evaluation deadlines as `TaskCanceledException`; an individual deadline does not end the whole phase. Caller cancellation, document replacement, detach and invalid native frames stop the relevant probe; script failures are not retried. Exhaustion reports `TimeoutException` with the last evaluation cancellation, and fixture failures and adapter diagnostic records retain full exception details. Deterministic adapter tests cover recovery, cancellation/retirement and both old-document terminal events.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.CefSharp.SmokeHost/Test-SmokeResults.ps1` to check result validation without launching a browser. Both Windows PowerShell and PowerShell 7 can run the scripts.

Logs are written to `artifacts/smoke/<version>-<platform>/` (`restore.log`, `clean.log`, `build.log` when output is present, `stdout.log`, `stderr.log`, and `results.jsonl`); `-OutputDirectory` overrides the location. CI uploads that directory as `chromium-<version>-<platform>` even on failure. Inspect failed `compatibility-case` records first, then the adjacent lifecycle, console and diagnostic records.

## Pinned reference and evidence

Run shared fixtures through [Run-Reference.cjs](../../scripts/compatibility/Run-Reference.cjs). It requires Playwright and local Edge/Tampermonkey source; the default pins 5.5.1/MV3 identity and content. The separate beta pin selects 5.6.6242. The original stable reference used Edge 156.0.4314.58; later fixed-stable observations use Edge 157.0.4341.42. The stable extension's minimum Chromium 120 remains independent of MonkeySharp's 84 baseline. See [fixed-reference observations](api-contract.md#pinned-reference-observations), [network differences](network.md) and [pin/acquisition instructions](../../scripts/compatibility/README.md#pinned-beta-enhancements).

[Fixtures](../../scripts/compatibility/fixtures/), the [evidence index](../../scripts/compatibility/evidence/README.md) and the [corpus manifest](../../scripts/compatibility/corpus.json) are tracked together with the runners. The [differential schema](../../scripts/compatibility/evidence/schema.json) applies to reference records; native matrices and delivery summaries retain their own record shapes. Preserve original fixture bytes, hashes, versions, ordered events, settlements, effects and failed checks. Raw logs and browser profiles stay in ignored `artifacts/`.

## Latest recorded validation

The latest committed [enhanced delivery evidence](../../scripts/compatibility/evidence/enhanced-follow-up-delivery-p1.json) records implementation `1ef4c4f`, a documentation-only source patch and raw run `artifacts/p2-delivery/1d2373523e2f`. The matrix below describes that recorded run on 2026-11-11; documentation reorganization does not constitute another native run.

| CefSharp | Platform | .NET | JavaScript | SmokeHost/CDP + normal/private OOPIF | WPF | NuGet/public consumer | Source + package corpus |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 84.4.10 | x64 | 422 passed | 129 passed | Passed | Passed | Passed | 10 + 10, declared scope |
| 84.4.10 | x86 | 373 passed | 129 passed | Passed | N/A | Passed | 10 + 10, declared scope |
| 121.3.70 | x64 | 422 passed | 129 passed | Passed | Passed | Passed | 10 + 10, declared scope |
| 121.3.70 | x86 | 373 passed | 129 passed | Passed | N/A | Passed | 10 + 10, declared scope |
| 151.3.240 | x64 | 422 passed | 129 passed | Passed | Passed | Passed | 10 + 10, declared scope |
| 151.3.240 | x86 | 373 passed | 129 passed | Passed | N/A | Passed | 10 + 10, declared scope |

Each x64 .NET total contains 305 Core, 68 adapter and 49 Demo tests; x86 excludes Demo. Every combination includes 15 SmokeHost compatibility cases and normal/private OOPIF records without runtime diagnostics. The 120 source/package corpus executions retain individual outcomes: each 84 run has six passed cases and four expected engine limitations; each 121/151 run has eight passed cases and two expected engine limitations. These limitations are not functional passes.

The public consumer verifies a 350003-byte staged upload, split UTF-8 incremental text, menu callback fields and prepared GM inside an actual iframe target. Native OOPIF checks additionally cover script isolation, nested targets, migration, denied requests, private jars and balanced release. The gate rejects unexplained initialization/dispatch diagnostics and preserves full exception stacks. These checks do not substitute for fixed-TM native menu input or missing download/notification interactions.

The earlier [stable scoped delivery](../../scripts/compatibility/evidence/stable-follow-up-delivery-p0.json) and [2026-11-07 snapshot](../../scripts/compatibility/evidence/p2-delivery.json) remain historical evidence with their original scope. The [evidence index](../../scripts/compatibility/evidence/README.md#delivery-snapshots) distinguishes those snapshots and retained superseded matrices. Full M1 and full menu acceptance remain incomplete; request partitions, audible, historical MV2, strict start and transparent BFCache retain their own limitations. See [current evidence gaps](README.md#known-evidence-gaps).

## Corpus intake

The [pinned corpus](../../scripts/compatibility/corpus/README.md) contains two unmodified MIT-licensed upstream scripts with immutable commit URLs, exact source and license hashes, no external requirements, controlled loopback pages and seven separate category scenarios. CSP/cancellation/policy denial add variants. Source SmokeHost and NuGet-only consumers run the same actions. Fixed TM runs are separate; missing browser download permission remains blocked and the history-refresh reference assertion remains failed. Chromium 84's observed replaceAll failure is an explicit engine restriction, not a runtime pass. See [corpus evidence](../../scripts/compatibility/evidence/README.md#corpus-results).

The manifest's `localOriginal.sourcePath` preserves historical provenance for an optional user-provided local script. Its source is not redistributed and is not required in a fresh checkout; use `-CorpusScript` to supply an available script separately from the pinned seven-category corpus.

The source baseline runs normal/private OOPIF on every selected engine/platform, and delivery retains native-probes records before removing successful worktrees. Source and public package consumers use the same pinned corpus inputs.
