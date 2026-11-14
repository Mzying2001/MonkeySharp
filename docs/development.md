# MonkeySharp Development Guide

**English** | [中文](development.zh-CN.md)

Use this guide to build, package, verify and maintain the repository. For embedding the runtime, start with the [integration reference](integration-reference.md). [Validation](compatibility/validation.md) explains what each gate establishes and records dated results; [tooling instructions](../scripts/compatibility/README.md) own runner parameters and reference inputs.

## Environment and gate selection

For a clean source baseline, run `./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10` (including WPF on x64). For the full native/package gate, use `./scripts/compatibility/Run-Delivery.ps1`; it isolates each version/platform, verifies packages and runs public-API consumers. `-CorpusScript <path>` optionally adds a local script unchanged; `-PackageOnly` skips baseline checks and is diagnostic only. See [validation](compatibility/validation.md#reproduction) for scope and [tooling instructions](../scripts/compatibility/README.md) for parameters.

CI runs on PRs targeting `dev` or `main`, with CefSharp `84.4.10` and `156.0.150` on x64/x86; WPF gates run on x64 only. Its toolchain is Windows Server 2022, .NET SDK `9.0.315` and Node.js `24`, tested versions rather than minimum requirements. The SDK must support `MonkeySharp.slnx`; Core tests target `net8.0`, while CefSharp tests/apps target `net462`. Real-Chromium gates require a Windows desktop session.

## Build and test

CI and local baselines share `scripts/testing/Run-SourceTests.ps1` (clean/build, per-project .NET TRX, bounded JavaScript and runner/parser checks) and `scripts/testing/Run-NativeTests.ps1` (SmokeHost/CDP, OOPIF, corpus and x64 WPF). Both accept `-Platform`, `-CefSharpVersion` and `-OutputDirectory`. `Run-Baseline.ps1` composes them. Fresh evidence directories preserve failed stages and process reports. Use `Run-RepeatedNative.ps1 -Case webRequest -Repeat 30` or `-Case navigation-menu -Platform x64` for diagnosis; all attempts remain recorded and any failure fails the run. The manual **Test stability** workflow uses the same runner.

Use the same `CefSharpVersion` property for restore, build, test, and package commands:

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=156.0.150
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=156.0.150 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=156.0.150 --no-build
node --test --test-timeout=30000 Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

Repeat the .NET commands with `-p:Platform=x86`. The x86 solution configuration excludes the x64-only Demo projects. Do not build different native versions or platforms concurrently in the same output tree.

## Package

Build release packages with [Build-Packages.ps1](../scripts/Build-Packages.ps1), using Windows PowerShell 5.1 or PowerShell 7 and .NET SDK 8+:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Build-Packages.ps1

# Optional: choose one platform, a pinned current engine and an empty output directory.
./scripts/Build-Packages.ps1 -Platforms x64 -CurrentCefSharpVersion 156.0.150 -OutputDirectory artifacts/release
```

On Windows, double-click [Build-Packages.bat](../scripts/Build-Packages.bat) to build with the defaults, or run `./scripts/Build-Packages.bat -Platforms x64 -OutputDirectory "artifacts/release packages"`. The wrapper locates the adjacent PowerShell script independently of the working directory, uses `-NoProfile -ExecutionPolicy Bypass` for that process, forwards the original arguments with `%*`, and returns the PowerShell exit code. It exits immediately when the build finishes, without pausing.

The default invocation builds one `Mzying2001.MonkeySharp.Core` package and four adapters: `Mzying2001.MonkeySharp.CefSharp84.x64`/`.x86` targeting `84.4.10`, and `Mzying2001.MonkeySharp.CefSharp.x64`/`.x86` targeting the pinned current version `156.0.150`. Core and all adapters share the version in [Version.props](../Version.props); update that file for a version bump, or pass `-Version <version>` to override one build without editing source. With no `-CurrentCefSharpVersion`, the script reads the adapter project default. It never resolves a floating latest version or publishes packages.

The script defaults to Release and both platforms; `-Configuration` selects Release or Debug. Relative `-OutputDirectory` paths are repository-relative and must be empty. The default is a new `artifacts/packages/<version>-<run>/` directory. Each invocation isolates restore/build outputs by engine/platform under `artifacts/package-build/<run>/`, stops on native-command failures, and checks every package identity, Core target framework, transitive Core dependency, exact CefSharp dependency and symbol package. `packages.json` records paths, versions, platforms and SHA-256 hashes for the `.nupkg` files and their `.snupkg` files. Failed runs retain partial outputs for diagnosis; only a completed run writes the manifest. Package generation does not run the source/native gates; run validation before publication. CI runs this same script after its source and real-Chromium jobs pass.

Adapter packages retain the same assembly and namespace; install one variant per application. The Core project reference becomes a NuGet dependency, while `CefSharp.Common` is an exact public dependency such as `[84.4.10]`. The host selects a matching WinForms/WPF package. Only `84.4.10` adds `84` to the adapter package name; manually building other current versions must use a new MonkeySharp package version for publication.

After reviewing and validating the generated packages, create a nuget.org API key and publish Core first, then each selected adapter. For example, using the actual directory printed by the script:

```powershell
$packages = 'artifacts/packages/<version>-<run>'
$version = (Get-Content -LiteralPath "$packages/packages.json" -Raw | ConvertFrom-Json).version
dotnet nuget push "$packages/Mzying2001.MonkeySharp.Core.$version.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
dotnet nuget push "$packages/Mzying2001.MonkeySharp.CefSharp84.x64.$version.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
dotnet nuget push "$packages/Mzying2001.MonkeySharp.CefSharp.x64.$version.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
```

Repeat for x86 when releasing both platforms. `dotnet nuget push` also pushes the adjacent symbol package. Published package ID/version pairs cannot be replaced; use a new MonkeySharp version for a subsequent release.

For manual packaging, put Core and each adapter platform into separate directories. Switch engine/platform in an independent output tree or clean before reuse. Keep `Version` and `PackageVersion` consistent for Core and its adapter dependencies when overriding the project version. Do not pass a global `PackageId` override to the adapter: it can also propagate to the referenced Core project.

Both solution platforms map Core to AnyCPU, with outputs under `bin/Release/<framework>/`. Use `-p:Platform=AnyCPU` when packing Core, including with `--no-build` to reuse solution outputs. The CefSharp adapter requires its explicit x64 or x86 platform.

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=156.0.150 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=156.0.150 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=156.0.150 -o artifacts/packages/x86
```

## Publish to NuGet with GitHub Actions

[Publish NuGet](../.github/workflows/publish.yml) is a manually dispatched workflow. It calls the existing CI workflow to run the `84.4.10`/`156.0.150` x64/x86 .NET/JavaScript and real-Chromium/WPF gates, builds all five packages with the requested version, and tests publication logic without network access. The publish job downloads those exact artifacts, validates their manifest/version/SHA-256 hashes, then exchanges GitHub OIDC identity for a temporary NuGet credential with `NuGet/login@v1`. Only that job has `id-token: write` and `contents: write`; it uses the GitHub environment `nuget`. See the official [NuGet Trusted Publishing guide](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing).

Complete this setup once:

1. Commit and push the workflow and scripts to the repository's default branch so GitHub displays the **Run workflow** button.
2. In GitHub **Settings → Secrets and variables → Actions → Variables**, create the repository variable `NUGET_USER` containing the publishing user's nuget.org profile name, not an email address or an assumed GitHub username. No stored `NUGET_API_KEY` secret is needed.
3. In GitHub **Settings → Environments**, create the environment `nuget` used by the publish job.
4. Sign in to nuget.org, open the username menu, choose **Trusted Publishing**, and create a GitHub policy with the following values. Choose the individual/organization that owns the packages as the policy owner; existing package IDs must belong to that owner. Enable both new-package publishing and publishing new versions, scoped to `Mzying2001.MonkeySharp.*`.

| NuGet policy field | Value |
| --- | --- |
| Repository Owner | `Mzying2001` |
| Repository | `MonkeySharp` |
| Workflow File | `publish.yml` (filename only) |
| Environment | `nuget` |
| Package glob | `Mzying2001.MonkeySharp.*` |

When using a fork, substitute its actual GitHub owner/repository. The NuGet profile in `NUGET_USER` must have access to the selected policy owner. If NuGet marks the policy as temporarily active, complete the first publication during that activation window or reactivate it before running the workflow.

For each release, open GitHub **Actions → Publish NuGet → Run workflow**, choose the source branch/ref and enter a new three-part version, optionally with a prerelease suffix, without a `v` prefix. The input overrides the shared package version for that build without editing project files. Publication starts automatically after all gates pass, using the selected revision's artifacts. After all package and symbol pushes succeed, the workflow uses the built-in `GITHUB_TOKEN` to create a GitHub Release with the version as both its tag and title, a new tag pointing to the built commit, and all five `.nupkg` and five `.snupkg` files as assets. Versions with a prerelease suffix are marked as GitHub prereleases. Ordinary PR CI cannot publish.

[Publish-Packages.ps1](../scripts/Publish-Packages.ps1) checks all ten files before any upload and resolves manifest paths relative to the downloaded artifact, so build-runner absolute paths are never reused. It uploads Core before the adapters, disables implicit symbols on main-package pushes, then explicitly uploads each `.snupkg` with its symbol credential. Both phases use `--skip-duplicate`, so a duplicate main package does not prevent a missing symbol package from being retried. It stops on other CLI failures and writes credential-free `publish-results.json`, uploaded as the `nuget-publish-results` artifact. Its `accepted-or-existing` status means the server accepted the push or the package already existed; it does not mean asynchronous NuGet validation/indexing has completed.

If a publication is interrupted, use **Re-run failed jobs** on the same workflow run to reuse the existing `nuget-packages` artifact and obtain a new temporary credential. An existing GitHub Release for that version is reused, with missing assets uploaded and same-name assets overwritten; an existing tag is not moved. The `nuget-publish-results` artifact is uploaded even if NuGet publication or the GitHub Release step fails. Do not change binaries while reusing an already published version. NuGet cannot publish the five packages as one transaction. Check package availability and validation status on nuget.org after the workflow completes.

Local verification, without credentials or uploads:

```powershell
$packages = 'artifacts/release'
$version = (Get-Content -LiteralPath "$packages/packages.json" -Raw | ConvertFrom-Json).version
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Publish-Packages.ps1 -PackageDirectory $packages -Version $version -ValidateOnly
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PublishPackages.ps1 -PackageDirectory $packages
```

The test runner uses a temporary copy of built packages and shadows `dotnet` with an in-process stub; it verifies relocated artifacts, missing credentials, version/hash/incomplete-release rejection before uploads, Core-first independent symbol uploads, and partial failure reports. CI executes these checks against the packages it builds.

## Real-Chromium gates

Run the real-Chromium gates from a Windows desktop session:

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 156.0.150
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 156.0.150
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 156.0.150
```

Use the same version value throughout validation: `-p:CefSharpVersion=<version>` for `dotnet restore/build/test/pack`, and `-CefSharpVersion <version>` for the PowerShell runners. Run the SmokeHost commands for x64 and x86 at `84.4.10` and `156.0.150`; the Demo gate covers x64 at both versions and runs the default English UI once per version. Each Demo report records `environment.uiLanguage` and verifies English windows and permission controls. Other languages are covered by localization unit tests; see [interface language and deployment](../Mzying2001.MonkeySharp.Demo/README.md#interface-language). When switching versions in an existing checkout, first run `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<target-version>` (use the target platform) to avoid mixing incremental outputs from different CefSharp versions. The SmokeHost runner already restores, cleans and builds with the selected version, then verifies the running assembly version and process platform. Both gates use isolated profiles and `TrustedPageWorld`; they are integration checks, not security sandboxes.

## Chromium Compatibility Script Package

The SmokeHost installs repository-owned userscripts against loopback HTTP fixtures. Its runner requires all 15 compatibility cases by default (`-Case webRequest` isolates request rules/navigation cleanup), checks actual CefSharp version/platform and cleanup, and preserves logs under `artifacts/smoke/<version>-<platform>/<run>/`. Override that path with `-OutputDirectory`.

See [SmokeHost cases and result validation](compatibility/validation.md#smokehost-cases-and-results) for case IDs, assertions, timeouts, parser checks and log triage. The [validation guide](compatibility/validation.md#latest-recorded-validation) owns recorded results and remaining gaps; [tooling instructions](../scripts/compatibility/README.md) own runner parameters and pinned-reference prerequisites.

## Runtime implementation

`UserScriptBridgeGateway.cs` owns authenticated dispatch, authorization, request budgets and execution cleanup. Its `.Bootstrap.cs` partial prepares hello data and resource packages; `.Storage.cs` groups snapshot/chunk/replay handling and value listeners. Storage transfer and listener state use the gateway's existing `_stateLock` and are removed by its central disposal and execution-ended paths.

## Native lifecycle maintenance

The host-facing sequence and diagnostic budgets belong to the [integration reference](integration-reference.md#lifecycle-and-diagnostics). The details below explain the native frame recovery paths in [CefSharpUserScriptHost](../Mzying2001.MonkeySharp.CefSharp/CefSharpUserScriptHost.cs); [read-only evaluation tests](../Mzying2001.MonkeySharp.CefSharp.IntegrationTests/ReadOnlyScriptEvaluatorTests.cs) and [native frame tests](../Mzying2001.MonkeySharp.CefSharp.IntegrationTests/NativeFrameProbeTests.cs) cover their failure boundaries.

The host's main partial file owns attachment and document lifecycle. `CefSharpUserScriptHost.NativeCompatibility.cs` contains version-dependent native lookups; `.Menus.cs` keeps menu attestation and routing together; `.FrameSession.cs` contains per-document cancellation and serialization state. These files share the same host locks and ownership; keep lifecycle and cleanup ordering intact when editing them.

In mixed page/isolated-world hosts, asynchronous page-marker probes distinguish document navigation from isolated V8 context callbacks. Older CEF versions may reuse a frame ID and complete its release probe before its creation probe. The adapter retains a frame generation after release so the pending creation can initialize the new document; a newer document, main-page replacement or detach invalidates that generation. This bookkeeping does not retain the released document's capabilities or menu registrations.

CEF 84 may also deliver the previous process's main-frame wrapper during a cross-site context-created callback. If load completion instead identifies a different, uninitialized frame that matches the browser's native current main frame, the adapter retires the old page and initializes that frame before advancing load lifecycle stages. Child sessions already created in the new page are preserved only when the native parent chain confirms their ancestry under the new main frame. This recovery does not retry known failed or released frames and does not provide a document-start timing guarantee.

Before matching, the adapter reads the current renderer's `location.href`: older CefSharp context-created callbacks may retain the previous navigation URL. Canceled read-only evaluations are retried within a 15-second initialization budget, with at most two seconds per attempt and a 50 ms delay capped by the remaining budget. Cancellation, detach, replacement of the document session or native frame retirement stops initialization; a stale callback URL is never used as a fallback. An exhausted evaluation budget produces an initialization diagnostic containing a `TimeoutException` and the last cancellation. Failed lookup skips the document, including load fallback and context-menu execution. Document-start remains best effort.

## Documentation maintenance

The [documentation index](README.md#content-ownership) identifies the primary reference for each subject. Keep existing English/Chinese guides aligned in behavior and commands, preserve explicit compatibility anchors, and check both link resolution and whether the linked section answers the promised question. Contract claims need implementation/test support and, when comparing managers, the matching pinned reference evidence. Documentation changes do not refresh historical validation dates or results.
