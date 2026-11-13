# Compatibility validation tools

This directory contains the shared validation harness and its inputs. Maintained contracts, capability limits and reproduction guidance live in [docs/compatibility](../../docs/compatibility/README.md). Commands below run from the repository root.

| Entry | Purpose |
| --- | --- |
| `Run-Baseline.ps1` | Clean restore/build/.NET/JS, real SmokeHost/CDP, normal/private OOPIF, pinned source corpus and x64 WPF gate for one engine/platform |
| `Run-Delivery.ps1` | Isolated version/platform worktrees, baseline gates, package checks and public-API consumers |
| `Run-Reference.cjs` | Original fixture comparisons with a locally installed pinned Tampermonkey |
| `Run-Corpus.ps1`, `Run-Corpus.cjs` | Build/run pinned third-party scenarios through a source host, package consumer or fixed reference |
| `Run-NativeProbe.ps1` | Native OOPIF, BFCache and start/CSP capability experiments |
| `ReferenceEnvironment.cjs`, `Unpack-Reference.cjs` | Verify pinned source identity/content and unpack a signed beta package |
| `reference-stable.json`, `reference-beta.json` | Separate complete environment pins for stable 5.5.1 and beta 5.6.6242 |
| `PackageConsumer/` | Standalone consumer built against generated NuGet packages |
| `fixtures/` | Original userscripts and companion content/URL scenarios; shared by reference runners and automated tests |
| [Evidence index](evidence/README.md) | Test baselines, native/reference results, delivery snapshots and retained failures |
| `corpus.json`, `corpus/` | Pinned unmodified third-party source, licenses, scenarios and optional historical local-script provenance |

[Browser-independent Core/JS commands](../../docs/compatibility/validation.md#browser-independent-checks) can run without CefSharp, local notes or a local corpus script.

## Native gates

Use Windows, a desktop session for real Chromium/WPF gates, an SDK supporting `.slnx`, and matching CefSharp managed/native packages. The [development guide](../../docs/development.md) / [中文](../../docs/development.zh-CN.md) owns toolchain, source build and packaging details; [validation](../../docs/compatibility/validation.md#reproduction) explains gate selection and result scope.

```powershell
./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10
./scripts/compatibility/Run-Delivery.ps1
```

Baseline defaults are x64 and 84.4.10; accepted versions are 84.4.10 and 156.0.150. WPF runs for x64 only. Delivery defaults to these two versions and both platforms. `-CefSharpVersions` and `-Platforms` select subsets; `-CorpusScript <path>` adds an existing local script, `-KeepWorktrees` keeps successful checkouts, and `-PackageOnly` skips baseline checks for diagnostics. Keep different native versions in separate output trees. Historical evidence for 121/151 remains archived. For release package generation without native gates, use [Build-Packages.ps1](../../docs/development.md#package).

Raw baseline output is printed by the runner; SmokeHost saves its logs under `artifacts/smoke/<version>-<platform>/`. Delivery stores reports/packages under `artifacts/delivery/<run>/`. These generated directories and temporary browser profiles are ignored by Git.

Delivery creates independent short-path worktrees, assets, profiles and unique package versions. It snapshots tracked changes against the base commit and records the source-patch SHA-256; untracked files are not included in that patch. It stops on failure and preserves failed worktrees. Successful worktrees are removed after validating their resolved paths unless `-KeepWorktrees` is set. The public consumer resolves the generated NuGet packages rather than project references. An omitted `-CorpusScript` does not require local notes or an uncommitted script.

## Pinned reference

Install Playwright in an available Node module location. `PLAYWRIGHT_MODULE` can name that module. Supply an Edge executable and an existing unmodified Tampermonkey 5.5.1/MV3 extension directory; the runner checks the manifest pin and uses a fresh profile rather than copying user settings or scripts.

```powershell
node scripts/compatibility/Run-Reference.cjs C:/path/to/msedge.exe C:/path/to/tampermonkey
node scripts/compatibility/Run-Reference.cjs C:/path/to/msedge.exe C:/path/to/tampermonkey artifacts/compatibility/prepared-content.json scripts/compatibility/fixtures/prepared-content.user.js
```

Positional arguments are `<Edge-exe> <extension-dir> [output.json] [fixture.user.js] [environment.json]`. Defaults are a timestamped output, `scripts/compatibility/fixtures/returns.user.js` and tracked `reference-stable.json`. Existing output files are never overwritten. Fixture paths are repository-relative or absolute; relative output paths use the current working directory. The server supplies companion content from `fixtures/content/`. Interactive effects need their own recorded checks; no event is not proof of completion.

The environment JSON pins `channel` (stable/beta), `version`, `manifestVersion`, `extensionId`, `contentSha256` and records `settings`. Identity comes from the manifest public key. Content fingerprints hash the sorted `[relativePath, fileSha256]` JSON array, excluding Chromium's regenerated `_metadata` verification cache; symbolic links are rejected. Mismatches fail before launch. The browser uses a source copy and fresh profile. `settings.headless: false` selects a visible reference browser. Stable 5.5.1 and beta 5.6.6242 have separate tracked pins and recorded observations; see [fixed-reference scope](../../docs/compatibility/api-contract.md#pinned-reference-observations).

## WPF Demo E2E

The x64 baseline also invokes the [WPF Demo E2E runner](../../Mzying2001.MonkeySharp.Demo/README.md#verification), which runs once using the application's default English UI. Run it directly with `./Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 156.0.150`; `-NoBuild` reuses an already matching deployment. Demo JSON reports record `environment.uiLanguage` and English UI assertions under `artifacts/demo-smoke/`. Other languages are checked by localization unit tests without repeating the full browser suite.

## Pinned third-party corpus

```powershell
# Restore/clean/build the source host, then execute all pinned scenarios.
./scripts/compatibility/Run-Corpus.ps1 -Platform x64 -CefSharpVersion 156.0.150

# Use a built source host or a NuGet-only package consumer.
node scripts/compatibility/Run-Corpus.cjs --manager monkeysharp --host C:/path/to/MonkeySharp.PackageConsumer.exe --expected-cef-version 156.0.150 --expected-platform x64 --output artifacts/corpus/consumer-run

# Compare the same original sources and actions against pinned TM.
node scripts/compatibility/Run-Corpus.cjs --manager tampermonkey --browser C:/path/to/msedge.exe --extension C:/path/to/tampermonkey --output artifacts/corpus/reference-run
```

`Run-Corpus.ps1` accepts `-Platform`, `-CefSharpVersion`, `-OutputDirectory` and `-NoBuild`. Do not share native outputs between versions/platforms. `Run-Corpus.cjs` accepts `--case <scenario-id>` to select one scenario, `--environment <JSON>` for a complete reference pin, `--headed true` for manual browser interaction and `--permission-wait-ms <milliseconds>` to allow a manual download permission step. Do not treat elapsed time as permission. Output directories must be new.

`results.json` contains schema-1 differential records under `results`, including source/action/input hashes, environment and source patch, actual completed actions, failed action, checks, file/ZIP-entry hashes, diagnostics and server Cookie/method/status logs. Raw profiles, inputs and host logs stay beside it. Exit 0 accepts only `passed` and the explicitly recognized `expected-engine-limitation`; blocked/failed results exit 1. Manifest intake `ready` is separate from runtime status. The pinned corpus has seven main categories plus CSP, cancel and host-denial variants. See [scenario scope](corpus/README.md).

Baseline runs the source corpus. Delivery additionally runs it through every NuGet-only consumer; `-CorpusScript` remains the separate historical optional local-search script. Package-only diagnostics still do not establish full stable-reference acceptance.

## Maintaining shared inputs

Keep fixtures, corpus declarations and concise evidence in Git. Preserve hashed userscript LF bytes through `.gitattributes`; do not reformat binary or charset fixtures. Name fixtures and evidence by capability, keeping version and investigation qualifiers where they distinguish records. Update tests, runners and cross-record references together when renaming files. Use the [evidence index](evidence/README.md) to find test dependencies, current topic evidence, delivery snapshots and retained failures. Historical fixture hashes identify the inputs at the recorded commit and source patch, even when current fixture names or contents have changed. Preserve recorded commits, observed outcomes and historical artifact paths.

The optional local corpus source is not redistributed or required by automated Core/JS tests. Temporary plans and full implementation diaries belong in ignored `local-notes/`; raw output belongs in `artifacts/`. Promote current behavior and limitations into the [maintained documentation](../../docs/README.md).

## Pinned beta enhancements

The [beta pin](reference-beta.json) selects 5.6.6242 separately from stable and records its acquired package hash and publisher signature. Obtain that exact signed Chrome package from the official update service for gcalenpjmijncebpfijmoaglllgpjagf; the pin rejects a newer package or changed source. Run `node scripts/compatibility/Unpack-Reference.cjs <signed.crx> <new-directory>` to verify the publisher signature and restore only manifest.key from the signed publisher proof for unpacked loading. Executable source remains unchanged. Pass reference-beta.json as the fifth Run-Reference.cjs argument.

Recorded beta observations cover incremental text and stream inputs; fixed-beta native menu click/overlap comparisons remain incomplete. A pin establishes identity, not feature parity. See [network behavior and differences](../../docs/compatibility/network.md), [menu scope](../../docs/compatibility/host-interactions.md#menus) and [retained acquisition/probe failures](evidence/README.md#retained-failures-and-regressions).

## Native feasibility probes

`./scripts/compatibility/Run-NativeProbe.ps1 -Probe bfcache -Platform x64 -CefSharpVersion 156.0.150` builds and launches isolated native experiments, records actual version/architecture and rejects missing records or failed process exits. `-EnableBackForwardCache` tests explicit enablement; `-NoBuild` reuses an already matching build. Choose a new OutputDirectory for each attempt. A completed assessment does not turn an unobserved capability into supported behavior.

BFCache read-only polling treats a confirmed retired native frame as not yet ready and retries within its existing deadline. Errors from a still-valid frame remain failures; successful assessment does not establish that Chromium restored a cached page.

`Run-NativeProbe.ps1 -Probe oopif` tests distinct-site targets, flat public DevTools transport and actual isolated GM in normal/private contexts, including nested targets, migration, script/storage separation, permission denial and native cookie jars. Run-Baseline.ps1 invokes it on each selected engine/platform; it requires no external extension input.

`Run-NativeProbe.ps1 -Probe start-csp` runs prepared full-GM start, owned parser-pause and CSP effect experiments in normal/private main and OOPIF pages. Assessment success never implies strict start or a CSP bypass; see [decisions and candidate limits](../../docs/compatibility/start-csp-decision.md).

The public NuGet-only consumer also exercises incremental text progress, staged ReadableStream upload, the current typed menu callback contract and actual isolated OOPIF GM. Delivery retains native-probes output before worktree cleanup. A PackageOnly run remains diagnostic even when these checks pass.
