# MonkeySharp

## Project Overview

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. It separates browser-independent metadata parsing, URL matching, repository and storage management, permissions, asynchronous GM APIs, and bridge dispatch from the CefSharp browser-lifecycle adapter.

## Project Structure

- `docs/`: Integration reference and userscript API guide; see `integration-reference.md` for build, packaging, and E2E commands.
- `Mzying2001.MonkeySharp.Core/`: Browser-independent runtime, GM APIs, repository/storage abstractions, and bridge protocol.
- `Mzying2001.MonkeySharp.CefSharp/`: Browser lifecycle, JavaScript binding, script execution, and CefSharp host services.
- `Mzying2001.MonkeySharp.Demo/`: WPF browser and script manager with SQLite persistence, host services, and a real-Chromium `Run-E2E.ps1` gate.
- `Mzying2001.MonkeySharp.Core.Tests/`: Core unit tests and JavaScript bridge/API tests.
- `Mzying2001.MonkeySharp.CefSharp.IntegrationTests/`: Adapter integration tests using mocked CefSharp interfaces.
- `Mzying2001.MonkeySharp.Demo.Tests/`: Persistence, host-service, and script-manager tests.
- `Mzying2001.MonkeySharp.CefSharp.SmokeHost/`: Standalone WinForms host and `Run-E2E.ps1` for real-Chromium validation; built outside the main solution.

Use an explicit `x64` or `x86` platform for CefSharp builds. The Demo and its tests support only `x64` and are excluded from `x86` solution builds. Keep `CefSharpVersion` consistent across projects.

## Development Guidelines

Update related documentation and tests alongside code changes so they reflect the current implementation and requirements.

## Git Commit Conventions

- Use Conventional Commits: `<type>(<scope>): <summary>`, with concise English summaries.
- Optional scope: affected project, area, class, or file.
- For non-trivial changes, explain why and how in the body.
