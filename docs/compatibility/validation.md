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
