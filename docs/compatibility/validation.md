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

Run the original shared fixture against the fixed reference through [Run-Reference.cjs](../../scripts/compatibility/Run-Reference.cjs). It requires Playwright and locally available Edge/Tampermonkey installations; the runner verifies Tampermonkey 5.5.1/MV3. The committed reference environment used Edge 156.0.4314.58. That extension's minimum Chromium 120 is independent of MonkeySharp's 84 baseline.

[Fixtures](../../scripts/compatibility/fixtures/), [evidence](../../scripts/compatibility/evidence/) and the [corpus manifest](../../scripts/compatibility/corpus.json) are tracked together with the runners. The [differential schema](../../scripts/compatibility/evidence/schema.json) applies to reference records; native matrices and delivery summaries retain their own record shapes. Preserve original fixture bytes, hashes, versions, ordered events, settlements, effects and failed checks. Raw logs and browser profiles stay in ignored `artifacts/`.

## Recorded delivery snapshot: 2026-11-07

The [delivery record](../../scripts/compatibility/evidence/p2-delivery.json) reports all six 84/121/151 × x64/x86 isolated combinations passed within its declared scope. Each x64 target passed 403 .NET tests and its WPF gate; each x86 target passed 357 .NET tests. Each run had 114 JavaScript tests and 15 SmokeHost compatibility scenarios. WPF private-context coverage includes all three x64 engine versions.

This is a dated snapshot, not a new test result from reorganizing the repository. Its native results do not establish historical MV2 parity, unavailable audible/request-partition behavior or support for every third-party script. The pinned partition-deletion preservation failure remains failed; MonkeySharp's deliberate sibling-preservation difference is described in [Cookies](cookies.md#partitioned-cookie-scope).

## Corpus intake

Each third-party entry needs an immutable source URL, redistribution license, version, SHA-256, unmodified source and local prerequisites. The seven categories in the manifest remain pending: DOM decoration, page hooks, authenticated XHR, downloads, storage listeners, menu configuration and iframe/SPA. Project-authored fixtures do not replace that corpus.

The snapshot also ran a user-provided local about:blank search script unchanged through all six package consumers. It verified DOM creation, engine-choice storage, empty-submit validation and navigation persistence without a nonempty external search. Its source remains uncommitted because redistribution permission is undeclared. The manifest's `localOriginal.sourcePath` is historical provenance, not a required file in a fresh checkout; use `-CorpusScript` to supply an available script. The local script does not satisfy the seven pending pinned third-party categories.
