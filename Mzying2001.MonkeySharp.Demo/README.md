# MonkeySharp WPF browser demo

## Build and run

The Demo targets .NET Framework 4.6.2 and an **x64 process**. It references the Core and CefSharp projects, pins `CefSharp.Wpf` to the repository's default `121.3.70`, and uses `CommunityToolkit.Mvvm` 8.4.0 and `Microsoft.Data.Sqlite` 8.0.8. Build with the SDK selected by the root `global.json`. Windows needs a compatible .NET Framework installation and the x64 Visual C++ runtime required by CefSharp. Deploy the entire output directory, including native DLLs and the browser subprocess, rather than copying the EXE alone.

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
```

`CefSharpVersion` can be overridden for source builds; all managed and native CefSharp components must use the same version. The Demo includes the download handler signature compatibility needed by the repository's `151.3.240` CI configuration; this does not promise compatibility with arbitrary versions. Clean build outputs before switching `CefSharpVersion`; otherwise NuGet's native copy rules can retain DLLs from the previously built version. `Run-E2E.ps1` performs this clean automatically unless `-NoBuild` is supplied. It does not delete the application profile.

## Browser and manager

- New/close tab: Ctrl+T / Ctrl+W. Address bar: Ctrl+L. Reload: F5. DevTools: F12.
- Tabs retain their actual browser controls when switching; background tabs keep running. Each browser attaches its MonkeySharp host before entering the visual tree. Closing a tab awaits host teardown before disposing its browser.
- Closing the selected tab returns to its parent when one was assigned, otherwise to its left neighbor (or the next tab when closing the first). Closing a background tab keeps the current selection; closing the last opens a blank tab. Tab colors stay readable whether the tab strip or page has keyboard focus.
- Web popups and new-tab links open as managed tabs. Popup dimensions and a JavaScript `window.opener` relationship are not preserved.
- The script manager is a native WPF window. Create a script, import a UTF-8 `.user.js` file, or load an HTTP(S) URL into a draft. Review the source and use **安装 / 保存** to see its metadata and approve the requested permissions.
- The manager supports search, validation, editing, enable/disable, and deletion. Invalid metadata does not replace the installed version. Unsaved edits require confirmation before changing selection or closing the manager.
- Repository mutations refresh **all open tabs**, including frames, so old execution state is revoked and matching scripts run again. This intentionally favors correctness over avoiding reloads in the Demo.
- The current tab's registered commands appear under **脚本菜单**. Access keys can be invoked with Alt+key. **工具** opens data/download folders, diagnostics, download cancellation controls, or DevTools.

## Portable data and recovery

```text
<executable directory>/Data/
  .profile.lock
  monkeysharp.db
  BrowserCache/Default/
  UserScripts/<script-key>.user.js
  Dependencies/<cache-key>.json
  Downloads/
  Logs/demo.log
  Logs/cef.log
```

The EXE directory, not the process working directory, defines the profile. Startup fails with an actionable error if the directory is not writable or its profile is already open. Do not run as administrator just to bypass this check; use a writable application directory. There is no silent AppData fallback.

SQLite stores stable script keys, source locations/hashes, origins, timestamps, enabled state, canonical GM JSON values, and live tab state. Sources are UTF-8 files named by stable identity, not script-supplied names. Save operations stage and flush a source, retain a backup during the SQLite commit, and recover an interrupted update by comparing the committed hash. Deletion uses a tombstone until the database transaction commits. Recovery failures are reported in the diagnostics log; missing/invalid/hash-mismatched sources remain visible but disabled. They are never silently replaced with empty files.

Do not edit installed source files behind the running application: the hash check deliberately detects unreviewed external changes. Paste/import a reviewed source into the existing manager entry to recover its stable identity and values. Uninstall removes that script's stored values as well. Back up the entire `Data` directory **after closing the application**, including SQLite sidecar files if present. Log rotation retains one previous Demo log at approximately 5 MiB.

Core's new `IUserScriptRepositoryPersistence`, `UserScriptPersistenceRecord`, and `PersistentUserScriptRepository` are database-independent. Initialize the repository before attaching hosts. Durable implementations must not report cancellation after a successful commit; mutation notifications follow commit.

## API integration boundaries

| API group | Demo implementation |
| --- | --- |
| Values and value listeners | Shared SQLite store with canonical JSON, serialized writes, atomic snapshots and ordered change notifications; supports legacy storage mirrors. |
| DOM, info, log | Existing Core facade; logs are also shown in the diagnostics panel. |
| `@require`, `@resource` | Persistent per-installation/revision URL cache. HTTP(S), at most 10 redirects, 30-second timeout, 10 MiB per fetched item. |
| XHR, Cookie, webRequest | Existing CefSharp services follow the shared Chromium request context. Custom navigation handlers use the adapter's multiplexer, not a replacement request handler. |
| Menus | Per-tab command collections, callbacks, access keys and disposal on execution end. |
| Notifications | Nonmodal owned WPF windows, click/close/completion lifecycle and optional bounded HTTP(S) image loading. No system toast registration required. |
| Clipboard | Dispatcher/STA writes, Unicode text and CF_HTML, bounded retry on clipboard contention. |
| `openInTab` | Captures the originating tab, respects active/insert/setParent, reports user closure and supports explicit close. Releasing an execution's handle does not close an already opened user tab. |
| Download | Streaming HTTP(S), progress, Save As, cancellation and failure. Default destination is `Downloads`; names are sanitized, existing default files receive suffixes, and partial files are removed. The tab owns the service; navigation cancels execution-owned operations. |
| Tab state | JSON objects keyed by script and live UI tab identity, stable across document navigation. Closed/stale tabs are removed. Browser-session restoration is not implemented, so old tab state is cleared at startup. |

The source/asset downloader and GM download service use separate HTTP clients; they **do not inherit browser login cookies or credentials**. Use the browser's native download path for authenticated site downloads. XHR continues to use the adapter's browser-context-backed implementation. `DownloadRequest` only contains HTTP(S) URL/name/Save As; extra Tampermonkey download fields cannot be supplied through that existing contract.

This project does not change protocol-1 or patch the existing JavaScript facade. In particular, the existing legacy `GM_download().abort()` facade does not send a host abort operation; cancel through the Demo download panel or by ending the execution instead. The host implements `IDownloadOperation.Abort`, but that is not a claim of complete Tampermonkey callback/option compatibility. Very short legacy callback operations remain subject to the library's callback-registration timing. Automatic updates, cloud sync, bookmarks, full history, isolated worlds, script signatures, and extension-store installation are out of scope.

## Security

**TrustedPageWorld is enabled explicitly. Both installed scripts and visited pages must be trusted.** `@grant`, `@connect`, the permission policy and bridge tokens still apply, but page-world code can observe or use exposed capabilities. The install confirmation is user review, not a sandbox. The Demo must not be treated as a hardened browser or a drop-in Tampermonkey replacement.

The default `AllowDeclaredPermissionsPolicy` approves calls that pass the Core grant checks. A production host must supply its own trust policy and actual isolation if it needs protection from malicious pages. This Demo does not disable certificate validation or Chromium web security.

## Verification

```powershell
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
```

The E2E runner starts a real WPF application with loopback fixtures and a separate `SmokeRuns/<id>/Data` profile, never the normal browser profile. It leaves a JSON report under `artifacts/demo-smoke` and logs under the reported profile. It tests background tabs, tab switching, iframe injection, shared values/cookies, resources, XHR/webRequest, menus, notifications, real downloads, tab state, navigation, script enable/disable and final disposal. The test does not overwrite the system clipboard. Test-project shadow copying is disabled so SQLite's native library resolves from the deployed test output.

Manual acceptance checks: install/edit/delete through the manager; reject the permission prompt; close with unsaved edits; confirm/cancel Save As; write text and HTML to the clipboard from a trusted script; verify browser login persistence after restart; lock/remove a source file and check the recovery diagnostics; launch a second process with the same profile and check the startup error.
