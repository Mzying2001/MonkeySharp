# MonkeySharp

## Project Overview

MonkeySharp is a userscript runtime for applications that embed CefSharp. It separates browser-independent metadata parsing, URL matching, repository and storage management, permissions, asynchronous GM APIs, and bridge dispatch from the CefSharp browser-lifecycle adapter.

## Project Structure

- `docs/`: Guides and compatibility contracts; see `docs/README.md` for navigation and `docs/development.md` for build, packaging, and test commands.
- `scripts/`: Package build and publishing, test runners, and compatibility validation; see subdirectory READMEs for usage.
- `Mzying2001.MonkeySharp.Core/`: Browser-independent runtime and host contracts; `Js/` implements the GM JavaScript APIs.
- `Mzying2001.MonkeySharp.CefSharp/`: Browser lifecycle, JavaScript binding, CDP sessions and isolated execution, and CefSharp host services.
- `Mzying2001.MonkeySharp.Demo/`: WPF browser and script manager with SQLite persistence and host-service implementations.
- `Mzying2001.MonkeySharp.Core.Tests/`: Core unit tests; `JavaScript/` contains JavaScript bridge/API tests.
- `Mzying2001.MonkeySharp.CefSharp.IntegrationTests/`: Adapter integration tests using mocked CefSharp interfaces.
- `Mzying2001.MonkeySharp.Demo.Tests/`: Demo persistence, host services, and UI logic tests.
- `Mzying2001.MonkeySharp.CefSharp.SmokeHost/`: Standalone WinForms host for real-Chromium validation; built outside the main solution.

Use an explicit `x64` or `x86` platform for CefSharp builds. The Demo and its tests support only `x64` and are excluded from `x86` solution builds. Keep `CefSharpVersion` consistent across projects.

## Development Guidelines

- Update related documentation and tests alongside code changes so they reflect the current implementation and requirements.
- Keep documentation updates concise and readable; integrate necessary information into the relevant sections rather than appending detailed fix histories.
- Keep documentation paragraphs on single source lines, preserving Markdown structure.
- Task notes and temporary documents that do not need to be committed to Git may be stored in `local-notes/`; create the directory if needed.

## Git Commit Conventions

- Use Conventional Commits: `<type>(<scope>): <summary>`, with concise English summaries.
- Optional scope: affected project, area, class, or file.
- For non-trivial changes, explain why and how in the body.
