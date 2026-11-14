# Test execution helpers

CI and `scripts/compatibility/Run-Baseline.ps1` use `Run-SourceTests.ps1` and `Run-NativeTests.ps1`. Both select an explicit `-Platform x64|x86`, `-CefSharpVersion 84.4.10|156.0.150` and optional `-OutputDirectory`; different native combinations must use separate checkouts or run sequentially. Source checks retain TRX per test project, a two-minute test-host hang deadline and a 30-second JavaScript test deadline. Native checks include SmokeHost/CDP, normal/private OOPIF, pinned corpus and x64 WPF.

`Run-RepeatedNative.ps1 -Case webRequest -CefSharpVersion 84.4.10 -Platform x86 -Repeat 30` builds once, then launches a fresh native process for every attempt. `-Case navigation-menu` selects WPF and requires x64. `-NoBuild` reuses a matching deployment, whose actual engine/platform is still validated. Each attempt retains its logs and results; the summary records the first verdict, every failure and the exact commit/source patch. A later pass cannot turn a failed run green. The manual **Test stability** workflow uses this entry point without automatic retries in ordinary PR CI.

`TestProcess.ps1` runs a native command with a deadline, exact Windows argument quoting, streamed stdout/stderr files and a `.runner.json` report. Reports retain the exit code, timeout, process ID, duration and GitHub run/attempt; command arguments and environment credentials are not recorded. Timeouts terminate the owned process tree. Startup failures and incomplete redirected output remain failures.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/testing/Test-TestProcess.ps1` to verify argument preservation, output capture, nonzero exits, descendant cleanup and startup failures. The child fixtures use fresh temporary paths and do not access browsers or network services.

The native runners use fresh run directories by default. Demo runs copy logs and screenshots into their evidence directory even after failure; full browser profiles remain in the temporary profile directory named by the report. `Mzying2001.MonkeySharp.Demo/Test-SmokeResults.ps1` and the SmokeHost parser tests reject incomplete or inconsistent success reports independently of launching Chromium.

WPF popup readiness requires an open popup and visible item containers; cached containers alone do not establish readiness. UI automation invokes each command once and checks the resulting retention state. Dispatcher-based unit fixtures finish teardown before allowing the test host to unload their AppDomain.
