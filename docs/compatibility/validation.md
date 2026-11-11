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

Run shared fixtures through [Run-Reference.cjs](../../scripts/compatibility/Run-Reference.cjs). It requires Playwright and local Edge/Tampermonkey source; the default pins 5.5.1/MV3 identity and content. A complete separate environment pin can select a fixed beta. The original reference used Edge 156.0.4314.58; the [stable follow-up](p0-follow-up.md) uses Edge 157.0.4341.42. The extension's minimum Chromium 120 remains independent of MonkeySharp's 84 baseline.

[Fixtures](../../scripts/compatibility/fixtures/), [evidence](../../scripts/compatibility/evidence/) and the [corpus manifest](../../scripts/compatibility/corpus.json) are tracked together with the runners. The [differential schema](../../scripts/compatibility/evidence/schema.json) applies to reference records; native matrices and delivery summaries retain their own record shapes. Preserve original fixture bytes, hashes, versions, ordered events, settlements, effects and failed checks. Raw logs and browser profiles stay in ignored `artifacts/`.

## Recorded delivery snapshot: 2026-11-07

The [stable follow-up assessment](m1-report.md) / [中文](m1-report.zh-CN.md) reports the newer implementation and its separate acceptance scope. It does not overwrite this historical snapshot.

The WPF smoke poll keeps its 25-second deadline and actual predicates. A reload may cancel an in-flight JavaScript evaluation after its readiness check; polling retries that cancellation, while direct evaluations and other errors still fail. The regression checks verify eventual success, error propagation and persistent cancellation timing out. Native navigation/disable/enable assertions remain intact.

The [delivery record](../../scripts/compatibility/evidence/p2-delivery.json) reports all six 84/121/151 × x64/x86 isolated combinations passed within its declared scope. Each x64 target passed 403 .NET tests and its WPF gate; each x86 target passed 357 .NET tests. Each run had 114 JavaScript tests and 15 SmokeHost compatibility scenarios. WPF private-context coverage includes all three x64 engine versions.

This is a dated snapshot, not a new test result from reorganizing the repository. Its native results do not establish historical MV2 parity, unavailable audible/request-partition behavior or support for every third-party script. The pinned partition-deletion preservation failure remains failed; MonkeySharp's deliberate sibling-preservation difference is described in [Cookies](cookies.md#partitioned-cookie-scope).

## Corpus intake

The [pinned corpus](../../scripts/compatibility/corpus/README.md) now contains two unmodified MIT-licensed upstream scripts with immutable commit URLs, exact source and license hashes, no external requirements, controlled loopback pages and seven separate category scenarios. CSP/cancellation/policy denial add variants. Source SmokeHost and NuGet-only consumers run the same actions. Fixed TM runs are separate; missing browser download permission remains blocked. Chromium 84's observed replaceAll failure is an explicit engine restriction, not a runtime pass. See [follow-up evidence and remaining M1 requirements](p0-follow-up.md).

The snapshot also ran a user-provided local about:blank search script unchanged through all six package consumers. It verified DOM creation, engine-choice storage, empty-submit validation and navigation persistence without a nonempty external search. Its source remains uncommitted because redistribution permission is undeclared. The manifest's `localOriginal.sourcePath` is historical provenance, not a required file in a fresh checkout; use `-CorpusScript` to supply an available script. The local search script is separate from the new pinned seven-category corpus.

The enhanced consumer additionally verifies incremental text, staged stream inputs, menu event fields and GM inside a native iframe target using only public packaged APIs. The source baseline runs the normal/private OOPIF gate on every engine/platform, and delivery retains those native-probes records before removing successful worktrees.

See the [enhanced delivery assessment](m2-report.md) / [中文](m2-report.zh-CN.md) and its [versioned evidence](../../scripts/compatibility/evidence/enhanced-follow-up-delivery-p1.json) for the step 36 matrix. The OOPIF gate rejects unexplained initialization/dispatch diagnostics and retains full exception stacks. Earlier matrices that passed effects while retaining a disposed-frame MSC101 are preserved and superseded for final acceptance; M1 reference gaps and unselected P2 work remain explicit.
