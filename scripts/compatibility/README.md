# Compatibility validation tools

This directory contains the shared validation harness and its inputs. Maintained contracts, capability limits and reproduction guidance live in [docs/compatibility](../../docs/compatibility/README.md). Commands below run from the repository root.

| Entry | Purpose |
| --- | --- |
| `Run-Baseline.ps1` | Clean restore/build/.NET/JS, real SmokeHost/CDP and x64 WPF gate for one engine/platform |
| `Run-Delivery.ps1` | Isolated version/platform worktrees, baseline gates, package checks and public-API consumers |
| `Run-Reference.cjs` | Original fixture comparisons with a locally installed pinned Tampermonkey |
| `PackageConsumer/` | Standalone consumer built against generated NuGet packages |
| `fixtures/` | Original userscripts and companion content/URL scenarios; shared by reference runners and automated tests |
| `evidence/` | Concise versioned native/reference results and the differential record schema |
| `corpus.json`, `corpus/` | Pinned unmodified third-party source, licenses, scenarios and optional historical local-script provenance |

[Browser-independent Core/JS commands](../../docs/compatibility/validation.md#browser-independent-checks) can run without CefSharp, local notes or a local corpus script.

## Native gates

Use Windows, a desktop session for real Chromium/WPF gates, an SDK supporting `.slnx`, and the matching CefSharp managed/native packages. CI uses .NET SDK 9.0.315 and Node.js 24. See the [build reference](../../docs/integration-reference.md#build-package-and-verify) for framework and packaging details.

```powershell
./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10
./scripts/compatibility/Run-Delivery.ps1
```

Baseline defaults are x64 and 84.4.10; accepted versions are 84.4.10, 121.3.70 and 151.3.240. WPF runs for x64 only. Delivery defaults to all three versions and both platforms. `-CefSharpVersions` and `-Platforms` select subsets; `-CorpusScript <path>` adds an existing local script, `-KeepWorktrees` keeps successful checkouts, and `-PackageOnly` skips baseline checks for diagnostics. Keep different native versions in separate output trees.

Raw baseline output is printed by the runner; SmokeHost saves its logs under `artifacts/smoke/<version>-<platform>/`. Delivery stores reports/packages under `artifacts/p2-delivery/<run>/`. These generated directories and temporary browser profiles are ignored by Git.

## Pinned reference

Install Playwright in an available Node module location. `PLAYWRIGHT_MODULE` can name that module. Supply an Edge executable and an existing unmodified Tampermonkey 5.5.1/MV3 extension directory; the runner checks the manifest pin and uses a fresh profile rather than copying user settings or scripts.

```powershell
node scripts/compatibility/Run-Reference.cjs C:/path/to/msedge.exe C:/path/to/tampermonkey
node scripts/compatibility/Run-Reference.cjs C:/path/to/msedge.exe C:/path/to/tampermonkey artifacts/compatibility/prepared-content.json scripts/compatibility/fixtures/prepared-content.user.js
```

Positional arguments are `<Edge-exe> <extension-dir> [output.json] [fixture.user.js] [environment.json]`. Defaults are a timestamped output, `scripts/compatibility/fixtures/returns.user.js` and tracked `reference-stable.json`. Existing output files are never overwritten. Fixture paths are repository-relative or absolute; relative output paths use the current working directory. The server supplies companion content from `fixtures/content/`. Interactive effects need their own recorded checks; no event is not proof of completion.

The environment JSON pins `channel` (stable/beta), `version`, `manifestVersion`, `extensionId`, `contentSha256` and records `settings`. Identity comes from the manifest public key. Source content is hashed except regenerated `_metadata`; mismatches fail before launch. The browser uses a source copy and fresh profile. `settings.headless: false` selects a visible reference browser. An acquired beta needs a separate complete pin; 5.6.6242 remains pending. See [stable follow-up observations and fingerprint format](../../docs/compatibility/p0-follow-up.md).

## Pinned third-party corpus

```powershell
# Restore/clean/build the source host, then execute all pinned scenarios.
./scripts/compatibility/Run-Corpus.ps1 -Platform x64 -CefSharpVersion 121.3.70

# Use a built source host or a NuGet-only package consumer.
node scripts/compatibility/Run-Corpus.cjs --manager monkeysharp --host C:/path/to/MonkeySharp.PackageConsumer.exe --expected-cef-version 121.3.70 --expected-platform x64 --output artifacts/corpus/consumer-run

# Compare the same original sources and actions against pinned TM.
node scripts/compatibility/Run-Corpus.cjs --manager tampermonkey --browser C:/path/to/msedge.exe --extension C:/path/to/tampermonkey --output artifacts/corpus/reference-run
```

`Run-Corpus.ps1` accepts `-Platform`, `-CefSharpVersion`, `-OutputDirectory` and `-NoBuild`. Do not share native outputs between versions/platforms. `Run-Corpus.cjs` accepts `--case <scenario-id>` to select one scenario, `--environment <JSON>` for a complete reference pin, `--headed true` for manual browser interaction and `--permission-wait-ms <milliseconds>` to allow a manual download permission step. Do not treat elapsed time as permission. Output directories must be new.

`results.json` contains schema-1 differential records under `results`, including source/action/input hashes, environment and source patch, actual completed actions, failed action, checks, file/ZIP-entry hashes, diagnostics and server Cookie/method/status logs. Raw profiles, inputs and host logs stay beside it. Exit 0 accepts only `passed` and the explicitly recognized `expected-engine-limitation`; blocked/failed results exit 1. Manifest intake `ready` is separate from runtime status. The pinned corpus has seven main categories plus CSP, cancel and host-denial variants. See [scenario scope](corpus/README.md).

Baseline runs the source corpus. Delivery additionally runs it through every NuGet-only consumer; `-CorpusScript` remains the separate historical optional local-search script. Package-only diagnostics still do not establish full M1.

## Maintaining shared inputs

Keep fixtures, corpus declarations and concise evidence in Git. Preserve hashed userscript LF bytes through `.gitattributes`; do not reformat binary or charset fixtures. Evidence names containing P0/P1/P2 identify historical runs and remain stable. Relocation does not change fixture hashes, recorded commits, statuses or historical artifact paths.

The optional local corpus source is not redistributed or required by automated Core/JS tests. Temporary plans and full implementation diaries belong in ignored `local-notes/`; raw output belongs in `artifacts/`. Promote current behavior and limitations into the [maintained documentation](../../docs/README.md).

## Pinned beta enhancements

The [beta pin](reference-beta.json) selects 5.6.6242 separately from stable. Download the exact signed Chrome package from the official update service for gcalenpjmijncebpfijmoaglllgpjagf (package hash in the pin). Run `node scripts/compatibility/Unpack-Reference.cjs <signed.crx> <new-directory>` to verify the publisher signature and restore manifest.key for unpacked loading without changing executable source. Then pass reference-beta.json as the fifth Run-Reference.cjs argument. See [P1 progress and acquisition caveats](../../docs/compatibility/p1-follow-up.md); old failed results remain intact.

## Native feasibility probes

`./scripts/compatibility/Run-NativeProbe.ps1 -Probe bfcache -Platform x64 -CefSharpVersion 121.3.70` builds and launches isolated native experiments, records actual version/architecture and rejects missing records or failed process exits. `-EnableBackForwardCache` tests explicit enablement; `-NoBuild` reuses an already matching build. Choose a new OutputDirectory for each attempt. A completed assessment does not turn an unobserved capability into supported behavior.

`Run-NativeProbe.ps1 -Probe oopif` tests distinct-site targets, flat public DevTools transport and actual isolated GM in normal/private contexts, including nested targets, migration, script/storage separation, permission denial and native cookie jars. Run-Baseline.ps1 invokes it on each selected engine/platform; it requires no external extension input.

`Run-NativeProbe.ps1 -Probe start-csp` runs prepared full-GM start, owned parser-pause and CSP effect experiments in normal/private main and OOPIF pages. Assessment success never implies strict start or a CSP bypass; see [decisions and candidate limits](../../docs/compatibility/start-csp-decision.md).
