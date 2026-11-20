# MonkeySharp changelog

**English** | [中文](changelog.zh-CN.md)

## 4.1.0 — 2026-11-20

- Added `CefSharpUserScriptHost.TryGetContextMenuScriptKey`. Custom CefSharp menu handlers can resolve an `@run-at context-menu` command ID to an installation key and use that key to select the correct icon when scripts share a name. The command ID is scoped to the active menu. [Integration reference](integration-reference.md#interaction-services) · [Git commit](https://github.com/Mzying2001/MonkeySharp/commit/2514967ec798caaa51e1e475bcd455881165a7af)

[Git changes since 4.0.0](https://github.com/Mzying2001/MonkeySharp/compare/4.0.0...4.1.0)

## 4.0.0 — 2026-11-17

### Breaking changes

- Replaced `InMemoryUserScriptRepository` and `PersistentUserScriptRepository` with `UserScriptRepository` backed by `IUserScriptPersistence`. Call `InitializeAsync` before using the repository or attaching the host. Custom persistence implementations now exchange typed installation and asset records and use source version checks for conditional writes. [Persistence contract](integration-reference.md#persistent-installations)
- Resource APIs derive text from the original bytes using the declared charset or UTF-8. Host resource providers supply bytes and encoding metadata. Dependency decoding is strict, and resource text decoding replaces invalid byte sequences. [Resource preparation](integration-reference.md#resource-integrity-and-update-checks)
- The Demo now stores installation metadata in SQLite schema 3, source in UTF-8 files, and shared dependency and resource bytes in a content-addressed cache. To start the Demo with a 3.x profile, back it up, rebuild the database, and reinstall scripts manually. [Demo data and recovery](../Mzying2001.MonkeySharp.Demo/README.md#portable-data-and-recovery)

### Added and changed

- Added independent source reload and asset refresh, along with conditional editor saves that preserve drafts when the source or revision changes. The Demo reports when an installation is enabled but unavailable.
- Separated enabled intent from preparation readiness. Unavailable installations stop executing, and their obsolete executions and static request rules are revoked. Reloaded scripts use the updated revision on subsequent navigation.
- Added recovery for interrupted Demo source writes and collection of unreferenced assets. Shared assets remain available to other installations.

[Git changes since 3.0.1](https://github.com/Mzying2001/MonkeySharp/compare/3.0.1...4.0.0)

## 3.0.1 — 2026-11-14

- Fixed HTTP requests and downloads to retain their original CefSharp request context across redirects. Downloads carry the invoking document for scoped `GM.webRequest` rules and remain active after an installation changes, subject to redirect authorization.
- Fixed cleanup of obsolete executions and their listeners when installations change. Isolated-world bridge calls use Core deadlines, avoiding a second timeout during permission prompts and body reads.
- Fixed isolated-world menu callbacks after same-document URL changes and restored the Demo's favicon lookup in the page DOM.
- Fixed script injection when `@antifeature` repeats a type and locale. The parser keeps the first declaration and warns about duplicates.
- Centralized the package version in `Version.props` and made build runners read the default CefSharp version from the project. NuGet publishing now also creates the GitHub release and attaches packages and symbols.

[Git changes since 3.0.0](https://github.com/Mzying2001/MonkeySharp/compare/3.0.0...3.0.1)

## 3.0.0 — 2026-11-14

- Split the browser-independent runtime into Core and moved CefSharp integration to a document- and frame-aware host. Added authenticated asynchronous bridge dispatch, grant checks, per-script DOM isolated worlds, and isolated execution in supported out-of-process iframes.
- Expanded Tampermonkey compatibility with modern `GM.*` and legacy `GM_*` APIs, synchronous legacy value and resource reads, bulk storage and change listeners, and grant-gated window helpers. Added native `@run-at context-menu` execution.
- Added persistent script installations, update checks, and preparation of `@require` and `@resource` before a revision activates, including declared integrity hash verification.
- Added CefSharp-backed HTTP requests, cookies, and scoped `GM.webRequest` cancellation and redirects. Added binary and stream upload support, incremental text responses, abortable downloads, tab audio controls, and partitioned cookie operations where the engine supports them.
- Added the WPF Demo with a multi-tab browser, SQLite persistence, a script manager with a syntax-highlighted editor, English and Chinese UI, and script menu commands grouped by script. The Demo supports private contexts and interactive permission prompts with remembered decisions.
- Published a shared Core NuGet package and separate x64 and x86 adapters for CefSharp `156.0.150` and `84.4.10`. Each adapter declares its exact CefSharp dependency.

[Git history for 3.0.0](https://github.com/Mzying2001/MonkeySharp/commits/3.0.0/)
