# MonkeySharp

## Project Overview

MonkeySharp 3 is a userscript runtime for applications that embed CefSharp. It separates browser-independent metadata parsing, URL matching, repository and storage management, permissions, asynchronous GM APIs, and bridge dispatch from the CefSharp browser-lifecycle adapter.

## Project Structure

- `Mzying2001.MonkeySharp.Core/`: Browser-independent runtime, domain models, metadata parsing, matching, storage, permissions, APIs, and bridge protocol.
- `Mzying2001.MonkeySharp.CefSharp/`: CefSharp adapter for frame lifecycle handling, JavaScript binding, and script execution.
- `Mzying2001.MonkeySharp.Core.Tests/`: Core unit tests and JavaScript protocol tests.
- `Mzying2001.MonkeySharp.CefSharp.IntegrationTests/`: CefSharp adapter integration tests.
- `Mzying2001.MonkeySharp.CefSharp.SmokeHost/`: WinForms real-Chromium smoke host and `Run-E2E.ps1` lifecycle/API gate.
- `MonkeySharp.slnx`: Main solution for building and testing the repository.
- `README.md`: Setup, supported APIs, security model, migration, and build documentation.
- `local-notes/`: Local design and implementation notes that are not committed to Git.

The Core project targets `net462`, `netstandard2.0`, and `net8.0`. The CefSharp adapter and smoke host target `net462` and must be built for an explicit `x64` or `x86` process platform. The adapter defaults to CefSharp `121.3.70`; set `CefSharpVersion` when compiling against another tested CefSharp release.

## Git Commit Conventions

- Use Conventional Commits: `<type>(<scope>): <summary>`
- Example types: `feat`, `fix`, `refactor`, `build`, `docs`, `test`, `chore`
- Use an optional scope for the affected project, area, class, or file
- Write concise English summaries
- For non-trivial changes, include a body explaining the reason and implementation/fix approach
