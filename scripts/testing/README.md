# Test execution helpers

`TestProcess.ps1` runs a native command with a deadline, exact Windows argument quoting, streamed stdout/stderr files and a `.runner.json` report. Reports retain the exit code, timeout, process ID, duration and GitHub run/attempt; command arguments and environment credentials are not recorded. Timeouts terminate the owned process tree. Startup failures and incomplete redirected output remain failures.

Run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/testing/Test-TestProcess.ps1` to verify argument preservation, output capture, nonzero exits, descendant cleanup and startup failures. The child fixtures use fresh temporary paths and do not access browsers or network services.

The native runners use fresh run directories by default. Demo runs copy logs and screenshots into their evidence directory even after failure; full browser profiles remain in the temporary profile directory named by the report. `Mzying2001.MonkeySharp.Demo/Test-SmokeResults.ps1` and the SmokeHost parser tests reject incomplete or inconsistent success reports independently of launching Chromium.
