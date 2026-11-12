# MonkeySharp WPF browser demo

## Build and run

The Demo targets .NET Framework 4.6.2 and an **x64 process**. It references the Core and CefSharp projects, pins `CefSharp.Wpf` to the repository's default `151.3.240`, and uses `CommunityToolkit.Mvvm` 8.4.0, `Microsoft.Data.Sqlite` 8.0.8, and AvalonEdit 6.3.1.120. Build with a compatible .NET SDK installed on the development machine. Windows needs a compatible .NET Framework installation and the x64 Visual C++ runtime required by CefSharp. Deploy the entire output directory, including native DLLs and the browser subprocess, rather than copying the EXE alone.

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe
```

`CefSharpVersion` can be overridden for source builds; all managed and native CefSharp components must use the same version. `Run-E2E.ps1` also defaults to `151.3.240`. The Demo includes the download handler signature compatibility needed by the repository's `84.4.10`, `121.3.70`, and `151.3.240` CI configurations; this does not promise compatibility with arbitrary versions. Clean build outputs before switching `CefSharpVersion`; otherwise NuGet's native copy rules can retain DLLs from the previously built version. `Run-E2E.ps1` performs this clean automatically unless `-NoBuild` is supplied. It does not delete the application profile.

## Code organization

The Demo keeps its existing WPF/MVVM layers. `Runtime` assembles profile resources and hosts; `TabContextManager` owns live tab contexts and the shared temporary private session, while `TabRuntime` owns one host, download service and browser. Teardown stops the browser, cancels downloads, detaches the host, drains accepted downloads, disposes the browser and finally releases its context. Repeated disposal and shutdown await the same operation.

`Views` contains windows and browser state binding. `Browser` contains the CefSharp navigation, popup, display and native download handlers plus tab-owned favicon discovery. The host must attach before the browser enters the visual tree. Favicon discovery is canceled when its tab view is disposed and never loads private-tab favicons outside CEF.

`ViewModels` coordinates tab selection, editing and script-manager actions. Draft loading updates source, origin and details together, even when importing identical source from a different file. `Services` contains individual WPF host adapters, bounded UTF-8 source reading and content hashing. `Persistence` retains SQLite transactions, typed storage, source staging and crash recovery; the database format and portable directory layout are unchanged.

`Automation` contains the executable's smoke scenarios, loopback fixture, userscript fixture, bounded browser polling and report writer. These internal types retain their existing runtime namespace. `App` selects normal or smoke startup; `Run-E2E.ps1`, `--smoke`, `--smoke-report`, report fields, check names and automation-facing control identifiers remain stable. Explicit automation names use English; visible labels and tooltips remain localized.

## Browser and manager

- New/close tab: Ctrl+T / Ctrl+W. Address bar: Ctrl+L. Reload: F5. DevTools: F12.
- Tabs retain their actual browser controls when switching; background tabs keep running. Each browser attaches its MonkeySharp host before entering the visual tree. Closing a tab awaits host teardown before disposing its browser.
- Closing the selected tab returns to its parent when one was assigned, otherwise to its left neighbor (or the next tab when closing the first). Closing a background tab keeps the current selection; closing the last opens a blank tab. Tab colors stay readable whether the tab strip or page has keyboard focus.
- Web popups and new-tab links open as managed tabs. Popup dimensions and a JavaScript `window.opener` relationship are not preserved.
- The script manager is a native WPF window. Create a script, import a UTF-8 `.user.js` file, or load an HTTP(S) URL into a draft. Review the source and use **安装 / 保存** to see its structured metadata and approve the requested permissions. `Ctrl+S` performs the same save/install action and the editor shows an unsaved-change indicator.
- Source editing uses AvalonEdit with line numbers, JavaScript highlighting (including async/await and template strings), four-space Tab / Shift+Tab indentation, inherited indentation on new lines, and Ctrl+Z / Ctrl+Y undo/redo. Long lines scroll horizontally without wrapping. Template strings are colored as strings, without separately highlighting interpolation expressions.
- Installed scripts appear as readable rows with status, version, description, and an inline enable/disable toggle. The manager supports search across name, description, namespace, and source, status filtering, name sorting, deletion, and a manual **check for updates and install** action. Invalid metadata does not replace the installed version. Unsaved edits require confirmation before changing selection or closing the manager.
- Metadata details are read-only and grouped into identity, matching rules, permissions, dependencies/resources, and diagnostics; edit the source to change metadata. The row prefers `@icon64` and then `@icon`; WPF loads valid HTTP(S) URLs directly and uses a first-letter placeholder when an icon is missing or cannot be loaded.
- Update checks prefer `@updateURL`, then `@downloadURL`; `@downloadURL none` disables checks. The downloaded script must have the same non-empty name and namespace and a newer version. Checks use the independent HTTP client without browser login cookies; there is no background update task.
- Repository mutations refresh **all open tabs**, including frames, so old execution state is revoked and matching scripts run again. This intentionally favors correctness over avoiding reloads in the Demo.
- The current tab's registered commands appear under **脚本菜单**. `@run-at context-menu` entries are added to the native page context menu only for matching frames; selecting one creates a fresh script execution. Access keys can be invoked with Alt+key. **工具** opens data/download folders, diagnostics, download cancellation controls, or DevTools.

## Portable data and recovery

The Demo uses SQLite schema format **2** and validates the database format at startup. An unsupported format causes a startup error; the existing database is left intact.

If startup reports an unsupported database format, close all Demo processes and back up the complete `Data/` directory. To start with an empty database, move `Data/monkeysharp.db` and its `-wal`/`-shm` companions out of the profile before restarting. Recreating the database does not restore installed scripts or GM values; source files are not automatically reimported. Custom storage providers must follow the [typed value contract](../docs/integration-reference.md#host-service-contracts).

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

SQLite stores stable script keys, source locations/hashes, origins, timestamps, enabled state, typed GM values (including stored undefined and non-finite numbers), and live tab state. Sources are UTF-8 files named by stable identity, not script-supplied names. Save operations stage and flush a source, retain a backup during the SQLite commit, and recover an interrupted update by comparing the committed hash. Deletion uses a tombstone until the database transaction commits. Recovery failures are reported in the diagnostics log; missing/invalid/hash-mismatched sources remain visible but disabled. They are never silently replaced with empty files.

Do not edit installed source files behind the running application: the hash check deliberately detects unreviewed external changes. Paste/import a reviewed source into the existing manager entry to recover its stable identity and values. Uninstall removes that script's stored values as well. Back up the entire `Data` directory **after closing the application**, including SQLite sidecar files if present. Log rotation retains one previous Demo log at approximately 5 MiB.

Core's `IUserScriptRepositoryPersistence`, `UserScriptPersistenceRecord`, and `PersistentUserScriptRepository` are database-independent. Initialize the repository before attaching hosts. Durable implementations must not report cancellation after a successful commit; mutation notifications follow commit.

## Private browsing

`GM.openInTab(url, {incognito:true})` and the legacy form request an actual private context. The Demo also provides a **私密＋** button and marks private tabs. Omitted `incognito` inherits the originating tab; false explicitly opens a normal tab. Hosts declare `ITabService.SupportsIncognito`; a private request is rejected before opening when the service does not support it.

The Demo's `TabContext` binds stable tab identity, the native RequestContext, privacy state, value store, permission policy and tab-state service. All private tabs in one live session share a separate in-memory RequestContext. Cookies and cache are isolated from normal browsing; permission decisions and tab data stay in memory. The last private tab closes and drains its host before releasing that context and clearing the private session. `@run-in`, `GM.info.isIncognito`, network APIs and tab opening all use that same actual context.

Private GM values follow the pinned Tampermonkey 5.5.1 temporary-session behavior: each script receives a copy of its normal baseline on first private access, then private changes and listeners stay within the private session. Writes do not return to normal storage or persist after the last private tab closes. This is a copied baseline, not live sharing. Tab-state enumeration is also separated by privacy context.

Private console/runtime diagnostics and tab data are not written to the normal profile. Automatic private favicon loading outside CEF is disabled, native file logging is disabled in the Demo, and private download history is removed when its tab closes. Files explicitly saved by a download and explicitly copied clipboard content remain user outputs. See the [reference and real-context evidence](../docs/compatibility/README.md#private-contexts).

## API integration boundaries

| API group | Demo implementation |
| --- | --- |
| Values and value listeners | Normal tabs use the shared SQLite store; private sessions copy the normal baseline once into isolated memory. Private writes never persist or flow back to normal tabs. The normal store with typed values, key insertion order, serialized writes, atomic snapshots and committed change notifications; supports legacy storage mirrors. |
| DOM, info, log | Existing Core facade; logs are also shown in the diagnostics panel. |
| `@require`, `@resource` | Persistent per-installation/revision URL cache. SRI `md5`/`sha256` hashes are checked against original bytes before caching and on cache reads. HTTP(S), at most 10 redirects, 30-second timeout, 10 MiB per fetched item. |
| Partitioned Cookie | CDP CRUD on tested 121/151; 84 rejects. XHR partition selection remains unsupported before sending. |
| XHR, Cookie, webRequest | Existing CefSharp services follow the shared Chromium request context. Custom navigation handlers use the adapter's multiplexer, not a replacement request handler. |
| Audio | The independent CefSharp service reads and changes native tab mute state. Listener removal, external host changes and partial capability reporting are covered; audible is omitted. |
| Menus | Per-tab command collections, callbacks, access keys and disposal on execution end. |
| Notifications | Nonmodal owned WPF windows with interaction-completion Promises, tag replacement, cancelable URL clicks, tab highlight, silent mode, timeout and bounded HTTP(S) images. No system toast registration required. |
| Clipboard | Dispatcher/STA writes, Unicode text and CF_HTML, bounded retry on clipboard contention. |
| `openInTab` | Captures the originating tab, respects active/insert/setParent/incognito, reports user closure and supports explicit close. Releasing an execution's handle does not close an already opened user tab. |
| Download | Streaming HTTP(S), progress, headers, conflict actions (`uniquify`, `overwrite`, `prompt`), cancellation, timeout and failure callbacks. Default destination is `Downloads`; names are sanitized, existing default files receive suffixes, and partial files are removed. The tab owns the service; navigation cancels execution-owned operations. |
| Tab state | JSON objects keyed by script and live UI tab identity, stable across document navigation. Closed/stale tabs are removed. Browser-session restoration is not implemented, so old tab state is cleared at startup. |

The source/asset downloader and update checker use independent HTTP clients without browser credentials. GM downloads and GM XHR share the same CEF HTTP service and RequestContext, including login cookies, anonymous requests and redirect authorization. GM.download returns an immediate abortable completion Promise; GM_download returns an abort-only handle. Both observe terminal events after file cleanup or atomic commit. Navigation mutes callbacks while accepted tasks continue; host disposal cancels and drains them. Background automatic updates, cloud sync, bookmarks, full history, isolated worlds, script signatures, and extension-store installation are out of scope.

## Security

**TrustedPageWorld is enabled explicitly. Both installed scripts and visited pages must be trusted.** `@grant`, `@connect`, the permission policy and bridge tokens still apply, but page-world code can observe or use exposed capabilities. The install confirmation is user review, not a sandbox. The Demo must not be treated as a hardened browser or a drop-in Tampermonkey replacement.

The default `AllowDeclaredPermissionsPolicy` approves calls that pass the Core grant checks. A production host must supply its own trust policy and actual isolation if it needs protection from malicious pages. This Demo does not disable certificate validation or Chromium web security.

## Verification

```powershell
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
```

The E2E runner starts a real WPF application with loopback fixtures and a separate `SmokeRuns/<id>/Data` profile, never the normal browser profile. It leaves a JSON report under `artifacts/demo-smoke` and logs under the reported profile. It tests background tabs, tab switching, iframe injection, shared values/cookies, undefined persistence across tab creation/navigation, resources, XHR/webRequest, menus, notifications, real downloads, tab state, navigation, script enable/disable and final disposal, including repeated tab disposal and application shutdown. The test verifies Unicode text and HTML on the system clipboard, then restores its previous contents. Test-project shadow copying is disabled so SQLite's native library resolves from the deployed test output.

CI runs the SmokeHost real-Chromium gate for CefSharp `84.4.10`, `121.3.70`, and `151.3.240` on x64 and x86. The WPF Demo gates cover all three versions on x64.

Manual acceptance checks: install/edit/delete through the manager; reject the permission prompt; close with unsaved edits; confirm/cancel Save As; write text and HTML to the clipboard from a trusted script; verify browser login persistence after restart; lock/remove a source file and check the recovery diagnostics; launch a second process with the same profile and check the startup error.
