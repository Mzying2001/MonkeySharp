# MonkeySharp WPF browser demo

**English** | [中文](README.zh-CN.md)

A portable WPF browser with background tabs, a native userscript manager, and SQLite storage for scripts and GM values.

## Build and run

The Demo targets .NET Framework 4.6.2 and **x64**. Building requires a compatible .NET SDK; running requires Windows, a compatible .NET Framework installation, and the x64 Visual C++ runtime required by CefSharp. Dependency versions are defined in the [project file](Mzying2001.MonkeySharp.Demo.csproj).

Run these commands from the repository root:

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe
```

Deploy the entire output directory, including the complete `CefSharp/` subdirectory, `MonkeySharpDemo.exe.config`, and the `zh-CN` satellite resource directory. CefSharp native dependencies, the browser subprocess, and resources are placed under `CefSharp/` using the official `CefSharpTargetDir` property; managed assemblies retain the package's default output behavior. Use a writable application directory: the Demo stores its portable profile under `Data/` beside the executable.

The default `CefSharpVersion` is `151.3.240`. Override it with `-p:CefSharpVersion=<version>` when building from source, using the same version for all managed and native components. Pass `-CefSharpVersion <version>` to `Run-E2E.ps1` to test that version. Clean outputs before switching versions to avoid retaining old native DLLs. The runner restores, cleans, and builds unless `-NoBuild` is supplied; the profile is preserved. See [Verification](#verification) for the tested versions.

## Interface language

Choose **Tools → Language (restart required)**, select **English** or **简体中文**, then close and reopen the Demo. In the Chinese interface, use **工具 → 语言（重启后生效）**.

The default is English. To override the saved language for one launch without changing the preference, use `--lang en` or `--lang zh-CN`:

```powershell
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe --lang zh-CN
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe --lang en
```

Windows-owned file-dialog controls follow the operating system's language. Script metadata, source code, and runtime or third-party diagnostic messages retain their original text.

## Browser and manager

- New/close tab: Ctrl+T / Ctrl+W. Address bar: Ctrl+L. Reload: F5. DevTools: F12.
- Switching tabs preserves their browser controls, and background tabs keep running. Closing the selected tab returns to its assigned parent or its left neighbor; closing the first selects the next tab. Closing a background tab keeps the selection, and closing the last opens a blank tab.
- Web popups and new-tab links open as managed tabs. Popup dimensions and JavaScript `window.opener` relationships are not preserved.
- Open **Script manager** to create a script, import a UTF-8 `.user.js` file, or load a draft from an HTTP(S) URL. Review the source and choose **Install / Save** or press Ctrl+S to review permissions and install it. Changing selection or closing the manager asks for confirmation when the draft has unsaved edits.
- The AvalonEdit source editor provides line numbers, JavaScript highlighting, four-space Tab / Shift+Tab indentation, automatic indentation, and Ctrl+Z / Ctrl+Y undo/redo. Long lines scroll horizontally. Template strings are colored as strings; interpolation expressions are not highlighted separately.
- The script list supports search by name, description, namespace, or source; status filters; name sorting; inline enable/disable toggles; and deletion. Invalid metadata prevents installation or replacement of the previous version.
- Metadata details are read-only and grouped into identity, matching rules, permissions, dependencies/resources, and diagnostics. Edit the source to change metadata. Icons prefer `@icon64`, then `@icon`; WPF loads HTTP(S) icons and uses a first-letter placeholder when unavailable.
- **Check and install updates** prefers `@updateURL`, then `@downloadURL`; `@downloadURL none` disables checks. Updates need the same non-empty name and namespace and a newer version. Checks use an independent HTTP client without browser login cookies. There is no background update task.
- Installing, editing, enabling, disabling, or deleting a script reloads **all open tabs**, including frames, to revoke old execution state and rerun matching scripts.
- Registered commands appear under **Script menu** and support Alt+access key. Matching `@run-at context-menu` entries appear in the native page context menu and start a fresh execution when selected. **Tools** provides diagnostics, download cancellation controls, DevTools, data/download folders, and language preferences.

## Portable data and recovery

The profile is stored beside the executable, independently of the process working directory:

```text
<executable directory>/Data/
  .profile.lock
  ui-language.txt
  monkeysharp.db
  BrowserCache/Default/
  UserScripts/<script-key>.user.js
  Dependencies/<cache-key>.json
  Downloads/
  Logs/demo.log
```

`ui-language.txt` stores the language choice; a missing, unreadable, or unsupported preference falls back to English. SQLite stores installed-script records, typed GM values, remembered permissions, and live tab state. Sources are UTF-8 files named by stable script identity. Browser-session restoration is not implemented, so old tab state is cleared at startup.

Startup fails if the directory is not writable or another process is using the profile. Use a writable application directory instead of elevating the process; there is no AppData fallback. Back up the complete `Data/` directory **after closing the application**, including SQLite `-wal`/`-shm` files if present.

The database uses schema format **2**. An unsupported format stops startup and leaves the database intact. To start with an empty database, first back up the profile, then move `Data/monkeysharp.db` and its `-wal`/`-shm` files out of the profile before restarting. This does not restore scripts or GM values, and source files are not automatically reimported.

Source changes use staging files, database transactions, and committed hashes for recovery. Missing, invalid, or hash-mismatched sources remain visible but disabled; recovery failures appear in diagnostics. Do not edit installed source files outside the running application. Import or paste reviewed source into the existing manager entry to retain its script identity and GM values. Deleting a script also removes its stored values.

Diagnostics are written to `Logs/demo.log`, which retains one previous log after reaching approximately 5 MiB. Native CEF file logging is disabled.

## Private browsing

Use **Private +**, or call `GM.openInTab(url, {incognito:true})`, to open a private tab. Omitted `incognito` inherits the originating tab; `false` explicitly opens a normal tab. Hosts declare private browsing support through `ITabService.SupportsIncognito`, and unsupported private requests are rejected before opening.

Private tabs share a separate in-memory RequestContext, isolated from normal cookies and cache. Permission decisions and tab data also stay in memory. Closing the last private tab cancels and drains its host operations before releasing the context and clearing the session. `@run-in`, `GM.info.isIncognito`, network APIs, and tab opening use the actual tab context.

Following the pinned Tampermonkey 5.5.1 temporary-session behavior, each script copies its normal GM value baseline on first private access. Subsequent private writes and listeners stay within the private session, do not flow back to normal storage, and disappear when the last private tab closes. Tab-state enumeration is also separated by privacy context.

Private console/runtime diagnostics and tab data are not written to the normal profile. Private favicon loading outside CEF is disabled, and private download history is removed when its tab closes. Explicitly saved downloads and copied clipboard content remain user outputs. See [private-context behavior and evidence](../docs/compatibility/README.md#private-contexts).

## API integration boundaries

| API group | Demo implementation |
| --- | --- |
| Values and value listeners | Shared SQLite storage for normal tabs, with typed values, insertion order, serialized writes, atomic snapshots, committed notifications, and legacy storage mirrors. Private sessions copy the normal baseline into isolated memory. |
| DOM, info, log | Core facade; logs also appear in the diagnostics panel. |
| `@require`, `@resource` | Persistent per-installation/revision URL cache. SRI `md5`/`sha256` checks use original bytes before caching and on reads. HTTP(S), up to 10 redirects, 30-second timeout, 10 MiB per item. |
| Partitioned Cookie | CDP CRUD on tested 121/151; rejected on 84. XHR partition selection is rejected before sending. |
| XHR, Cookie, webRequest | CefSharp services use the browser's actual RequestContext. Custom navigation handlers use the adapter's request-handler multiplexer. |
| Audio | Native mute-state reads, writes, and change listeners; audible state is unavailable. |
| Menus | Per-tab commands, callbacks, access keys, and cleanup on execution end. |
| Notifications | Nonmodal WPF windows with completion Promises, tag replacement, cancelable URL clicks, tab highlight, silent mode, timeout, and bounded HTTP(S) images. No system toast registration. |
| Clipboard | Dispatcher/STA writes for Unicode text and CF_HTML, with bounded contention retries. |
| `openInTab` | Respects active/insert/setParent/incognito, observes user closure, and supports explicit close. Releasing a script handle leaves the opened tab alive. |
| Download | Streaming HTTP(S), progress, headers, conflict actions (`uniquify`, `overwrite`, `prompt`), cancellation, timeout, and failure callbacks. Files default to `Downloads`, names are sanitized, and partial files are cleaned up. |
| Tab state | JSON objects scoped by script and live tab identity, preserved across navigation and removed when tabs close. |

Source/asset downloads and update checks use independent HTTP clients without browser credentials. GM downloads and GM XHR share the CEF HTTP service and RequestContext, including login cookies, anonymous requests, and redirect authorization.

`GM.download` immediately returns an abortable completion Promise; `GM_download` returns an abort-only handle. Completion follows file cleanup or atomic commit. Accepted downloads continue across navigation, script disable, and uninstall under captured authorization, while callbacks to the ended execution are muted; redirect targets still require approval. Closing the tab or disposing the host cancels and drains the tasks before releasing the browser context. See [download behavior](../docs/compatibility/downloads.md).

The Demo does not provide background automatic updates, cloud sync, bookmarks, full history, isolated execution worlds, script signatures, or extension-store installation. Full API parameters and contracts are in the [userscript guide](../docs/usage-guide.md) and [integration reference](../docs/integration-reference.md).

## Security

**TrustedPageWorld is enabled. Only run trusted scripts and visit trusted pages.** `@grant`, `@connect`, permission checks, and bridge tokens apply, but page-world code may observe or use exposed capabilities. Installation confirmation is a review step, not a security sandbox.

The Demo uses `InteractiveUserScriptPermissionPolicy` with `SensitiveUserScriptPermissionsPolicy`. Requests to `GM.xmlHttpRequest`, `GM.cookie`, and `GM.download` prompt for target-scoped consent unless a decision is already remembered. Choose **Allow once**, **Remember allow**, or **Deny and remember**. Open **Permissions** to review or revoke a script's remembered decisions; revocation reloads tabs. Consent cannot widen declared grants or target rules.

A production host needs a trust policy and actual isolation appropriate to its threat model. The Demo does not disable certificate validation or Chromium web security.

## Code organization

| Location | Responsibility |
| --- | --- |
| `Runtime/` | Profile initialization, host composition, tab contexts, shared private-session ownership, and shutdown. `TabRuntime` owns one browser, host, and download service. |
| `Views/`, `Browser/` | WPF windows, browser bindings, and CefSharp navigation, popup, display, download, and favicon handlers. |
| `ViewModels/` | Tab selection, drafts, script-manager actions, diagnostics, and display state. |
| `Services/`, `Persistence/` | WPF host services, HTTP source loading, SQLite transactions, typed storage, and source recovery. |
| `Localization/`, `Properties/` | Application language selection and lookup helpers; English and Chinese RESX resources. |
| `Automation/`, `Run-E2E.ps1` | Loopback fixtures, browser assertions, polling, isolated profiles, and JSON reports. |

Attach the MonkeySharp host before adding a browser to the visual tree. Tab disposal stops the browser, cancels downloads, detaches and disposes the host, drains accepted tasks, then releases the browser and context. Repeated disposal or shutdown awaits the same operation. Custom persistence implementations must follow the [host-service contracts](../docs/integration-reference.md#host-service-contracts).

`Properties/Resources.resx` contains English resources and `Properties/Resources.zh-CN.resx` contains Chinese resources. XAML uses `{loc:Text Key}`; C# uses `Strings.Get` / `Strings.Format`. Keep complete messages and matching placeholders in each translation. Additional languages need resource files, supported-language entries, menu options, and localization unit tests; the E2E runner remains English-only. Automation IDs stay independent of visible labels.

## Verification

Run the following from the repository root; real Chromium/WPF E2E requires a Windows desktop session:

```powershell
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
# Reuse an already matching build:
./Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -NoBuild
```

The runner launches one process using default English in an isolated `SmokeRuns/<id>/Data` profile. It checks UI labels, permissions, tabs and frames, values and cookies, resources, XHR/webRequest, menus, notifications, real downloads, private contexts, script changes, and final disposal. Clipboard checks temporarily write Unicode text and HTML, then restore the previous contents. Test-project shadow copying is disabled so SQLite's native library resolves from the test output.

JSON reports are written to `artifacts/demo-smoke`; `environment.uiLanguage` records the UI language, and `profile` identifies the run directory containing logs and screenshots. Other languages are checked by localization unit tests. CI runs the English Demo E2E once for each supported x64 CefSharp version: `84.4.10`, `121.3.70`, and `151.3.240`. SmokeHost additionally covers x86. See the [validation guide](../docs/compatibility/validation.md) for the wider matrix.

Manual checks: switch language and restart; override it for one launch with `--lang`; install/edit/delete scripts; reject permission requests; close with unsaved edits; confirm/cancel Save As; verify login persistence after restart; test missing or locked source recovery; and launch a second process against the same profile to check the startup error.
