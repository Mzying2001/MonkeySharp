# P0 Cookie backend

The default backend now uses the host's shared CDP session and remains bound to
the attached browser's RequestContext. No new DevTools observer is created for
Cookie calls. Each operation retains one session; detach invalidates it rather
than redirecting an in-flight call into another context.

The provider authorizes the URL before service access and filters returned
results again by name/domain/path, host-only rules and secure scope. Cookie
SameSite values map to CDP None/Lax/Strict, with unspecified omitted. Reads use
actual Chromium fields. Set checks protocol acceptance and reads back the
selected identity/attributes; delete selects one visible identity, sends its
exact domain and path, and verifies absence. A successful protocol response with
missing engine state is an error, covered by adapter tests. Same-name cookies
on other paths survive deletion.

The pinned Tampermonkey 5.5.1/MV3 experiment confirms modern set/delete resolve
undefined, legacy CRUD returns undefined, legacy GM_cookie is a function
namespace, and omitted path at /nested/page defaults to /nested. See
`evidence/tampermonkey-5.5.1-cookie-returns.json` and its original fixture.
The implementation follows those observations. The former reflective write
echo and custom addListener/removeListener APIs/types have been removed.

Current partitionKey/cookiePartition/firstPartyDomain requests are rejected
before effects at every supported version, including 84. Partitioned records
are excluded from this unpartitioned implementation. Partition support remains
step 19, not an implicit promise on newer Chromium.

Real validation on 2026-11-06:

- Complete 84 and 121/x64 baselines: 245 Core, 42 adapter and 38 Demo tests,
  85 JavaScript tests, SmokeHost/CDP, parser validation and real WPF E2E.
- SmokeHost/CDP at 84.4.10, 121.3.70 and 151.3.240, x64/x86. Cookie checks include
  modern/legacy CRUD and exact return values, session/expiry, host-only and domain
  cookies, SameSite, HttpOnly, authenticated/anonymous HTTP, actual HTTPS sending,
  Secure suppression over ordinary HTTP, multi-path exact deletion, deletion
  readback, and an engine-rejected __Host- cookie.
- A second real Chromium browser with a separate RequestContext starts without
  the primary marker; writes its own marker; sends only its own marker over HTTP;
  and leaves the primary marker unchanged. Both use the production CDP backend.
- The HTTP/TLS server uses a public test keypair. Certificate ignoring and DNS
  mapping are confined to SmokeHost. Chromium 121's HTTP-loopback Secure behavior
  differs from 84, so the transport gate uses a synthetic hostname mapped to the
  loopback server. No TLS result is inferred from a mock or from protocol success.
- The second browser is created and cleaned up on the existing WinForms UI
  thread. Creating it on a worker installs a SynchronizationContext without a
  message loop and can strand async continuations; the test gate explicitly
  marshals this phase onto the live UI thread.

Reproduce with `scripts/compatibility/Run-Baseline.ps1` and its
`-CefSharpVersion 121.3.70` variant, plus SmokeHost `Run-E2E.ps1` for the remaining
version/platform pairs. Raw logs: `artifacts/p0-step6-*`; committed actual
observations: `evidence/cookies-p0.json`.

Host migration: ICookieService now contains CRUD only. Direct CEF service users
pass an attached CefSharpUserScriptHost, rather than a standalone RequestContext.
There is no browser-global cookie event feed. Empty cookie names and arbitrary
first-party/partition extensions are outside the current input contract.
