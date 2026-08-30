# MonkeySharp

## Project Overview

MonkeySharp 2 is a userscript runtime for applications that embed CefSharp. It provides userscript metadata parsing, URL matching, storage, permissions, asynchronous GM APIs, and browser lifecycle integration.

## Project Structure

- `Mzying2001.MonkeySharp.Core/`: Browser-independent runtime, domain models, metadata parsing, matching, storage, permissions, APIs, and bridge protocol.
- `Mzying2001.MonkeySharp.CefSharp/`: CefSharp adapter for frame lifecycle handling, JavaScript binding, and script execution.
- `Mzying2001.MonkeySharp.Core.Tests/`: Core unit tests and JavaScript protocol tests.
- `Mzying2001.MonkeySharp.CefSharp.IntegrationTests/`: CefSharp adapter integration tests.
- `MonkeySharp.slnx`: Main solution for building and testing the repository.
- `README.md`: Setup, supported APIs, security model, migration, and build documentation.
- `local-notes/`: Local design and implementation notes that are not committed to Git.

## Git Commit Conventions

- Use Conventional Commits: `<type>(<scope>): <summary>`
- Example types: `feat`, `fix`, `refactor`, `build`, `docs`, `test`, `chore`
- Use an optional scope for the affected project, area, class, or file
- Write concise English summaries
- For non-trivial changes, include a body explaining the reason and implementation/fix approach
