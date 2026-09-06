# MonkeySharp

## Project Overview

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. It separates browser-independent metadata parsing, URL matching, repository and storage management, permissions, asynchronous GM APIs, and bridge dispatch from the CefSharp browser-lifecycle adapter.

## Project Structure

- `Mzying2001.MonkeySharp.Core/`: Browser-independent runtime, domain models, metadata parsing, URL matching, repository persistence abstractions, storage, permissions, GM APIs, and bridge protocol.
- `Mzying2001.MonkeySharp.CefSharp/`: CefSharp adapter for frame lifecycle handling, JavaScript binding, and script execution.
- `Mzying2001.MonkeySharp.Demo/`: WPF browser demo with background tabs, a native script editor/manager, SQLite persistence, host services, and a real-Chromium `Run-E2E.ps1` gate.
- `Mzying2001.MonkeySharp.Core.Tests/`: Core unit tests and JavaScript protocol tests.
- `Mzying2001.MonkeySharp.CefSharp.IntegrationTests/`: CefSharp adapter integration tests.
- `Mzying2001.MonkeySharp.Demo.Tests/`: Demo persistence and host-service tests.
- `Mzying2001.MonkeySharp.CefSharp.SmokeHost/`: Standalone WinForms real-Chromium smoke host and `Run-E2E.ps1` lifecycle/API gate, built separately from the main solution.
- `MonkeySharp.slnx`: Main solution containing Core, the CefSharp adapter, the WPF Demo, and their test projects; the x86 configuration excludes the x64-only Demo projects.
- `README.md` / `README.zh-CN.md`: English and Simplified Chinese setup, supported APIs, security model, migration, and build documentation.
- `Mzying2001.MonkeySharp.Demo/README.md`: Demo usage, portable data and recovery, host-service boundaries, and verification scenarios.
- `.github/workflows/`: CI build, test, and real-Chromium validation workflows.
- `local-notes/`: Local design and implementation notes that are not committed to Git.

The Core project targets `net462`, `netstandard2.0`, and `net8.0`; its tests target `net8.0`. The CefSharp adapter, adapter integration tests, and WinForms smoke host target `net462` and must be built for an explicit `x64` or `x86` process platform. The WPF Demo and its tests target `net462` and support only `x64`. CefSharp defaults to `121.3.70`; set `CefSharpVersion` consistently when compiling against another tested CefSharp release.

## Git Commit Conventions

- Use Conventional Commits: `<type>(<scope>): <summary>`
- Example types: `feat`, `fix`, `refactor`, `build`, `docs`, `test`, `chore`
- Use an optional scope for the affected project, area, class, or file
- Write concise English summaries
- For non-trivial changes, include a body explaining the reason and implementation/fix approach
