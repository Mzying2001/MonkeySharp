# P0 download validation

Both entry points use one client-identified operation. `GM.download` immediately
returns its original abortable completion Promise; `GM_download` returns an
abort-only handle. The host registers cancellation before awaiting operation
creation, subscribes to every event, then calls `Start`. Start acknowledgment
cannot complete the public Promise. Callback exceptions are reported independently.

`FileDownloadService` uses the HTTP service supplied by the adapter's download
factory, so Demo GM downloads and XHR share the same CEF URLRequest/RequestContext
and webRequest composition. The HTTP observer writes bounded chunks directly to
a temporary file. Success follows flush, close and atomic move/replace. Failure
and cancellation remove temporary files before the terminal notification.

Accepted tasks continue across document end, navigation, script disable and
uninstall under their captured authorization; redirect targets still require
host approval. Document callbacks are muted. Host disposal cancels tasks and Demo
drains them before releasing its browser context. This policy is deliberate and
is covered separately from JavaScript callback delivery.

Validation on 2026-11-06:

- The 84/x64 baseline passed 240 Core, 37 adapter and 38 Demo tests, 81 JavaScript
  tests, real SmokeHost/CDP, parser validation and WPF E2E.
- Real SmokeHost/CDP passed at CefSharp 84.4.10, 121.3.70 and 151.3.240, x64/x86.
  Each run verifies modern completion, legacy progress, transfer cancellation,
  timeout, HTTP failure, authenticated redirect, anonymous denial, zero bytes,
  immediate cancellation, disk failure before HTTP, partial cleanup, and
  completion after navigation. Server counts and actual file bytes are checked;
  the authenticated file SHA-256 is recorded in the evidence JSON.
- Real WPF E2E passed at 84 and 121/x64. The fixture endpoint requires the
  browser's login cookie. The modern Promise resolves with committed bytes and
  the host reads the complete file. The native SaveFileDialog is opened and
  closed in the test process; cancellation produces no HTTP request or file.
- Unit tests cover uniquify/overwrite contents, cancellation before Start,
  destination cancellation, missing-directory disk failure, zero-byte commit,
  synchronous completion during Start, abort while CreateAsync is pending,
  duplicate terminal signals and callback exceptions. These tests do not stand
  in for the real browser or picker checks above.

Reproduce with `scripts/compatibility/Run-Baseline.ps1`, the SmokeHost
`Run-E2E.ps1 -Platform x64|x86 -CefSharpVersion <version>`, and Demo
`Run-E2E.ps1 -CefSharpVersion <version>`. Raw logs are under
`artifacts/p0-step4-*`; committed observations are in
`evidence/downloads-p0.json`.

The supported completion details are `{url, name, loaded}` without local paths
or internal operation IDs. Blob/File transport remains step 13. Downloads do
not create a full browser-extension DownloadItem or an extension-whitelist
preference. The previously recorded document-start/isolated-world boundaries
are unchanged.
