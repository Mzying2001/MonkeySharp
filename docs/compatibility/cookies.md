# Cookies

The default service uses the attached browser's shared CDP session and actual RequestContext. Each operation retains one session; detach invalidates it rather than redirecting an in-flight request into a different context. Custom `ICookieService` backends provide CRUD; direct CEF service users construct `CefSharpCookieService(host)` with an attached host.

Core checks the authorized URL before access and filters returned name/domain/path, host-only and secure scope. SameSite maps to CDP None/Lax/Strict, with unspecified omitted. Set checks engine acceptance and reads back the selected identity and attributes. Delete selects one visible identity, expires its complete domain/path/partition identity and verifies absence. Same-name siblings survive. Protocol success without the expected engine state is an error.

Modern list resolves Cookie[]; modern set/delete resolve undefined. Legacy calls return undefined, with `(cookies, error)` for list and `(error)` for mutations. The pinned reference confirms the legacy function namespace and URL-directory default path. No reflection-based input echo or custom Cookie listener remains. See the [API guide](../usage-guide.md#cookies) / [中文](../usage-guide.zh-CN.md#cookies) and [host integration](../integration-reference.md#cookies).

## Partitioned Cookie scope

Explicit request Cookie support is separate from CRUD; see below.

| Engine | CRUD | Remaining limits |
| --- | --- | --- |
| 84.4.10 | Ordinary cookies | Explicit partitionKey is rejected before effects |
| 121.3.70 | Serializable partitionKey CRUD | hasCrossSiteAncestor is unavailable and rejected |
| 151.3.240 | Serializable partitionKey CRUD including hasCrossSiteAncestor | Opaque/nonce keys are unavailable |

Unspecified partitionKey selects ordinary cookies. Empty list selectors include serializable ordinary and partitioned identities; site selectors filter canonical sites. Mutations use the complete identity. Site normalization uses pinned ICANN/private suffix snapshots for the corresponding Chromium engine. On 151, omitted ancestor state is derived for set, unrestricted for list and defaults to true for delete; false must be explicit for same-site ancestor deletion.

The pinned reference deletes an ordinary same-name sibling when deleting a partitioned cookie. That preservation check remains failed in its record. MonkeySharp deliberately preserves the sibling; this is a documented difference. `firstPartyDomain` remains unsupported.

XHR `cookiePartition` is unsupported on every tested engine. The native URLRequest surface has no reliable top-level partition selector; probes sent ordinary state and stored incoming partitioned state under the request's own site. The option is rejected before network dispatch. Cookie-header synthesis and jar swapping are not used to simulate request-partition support.

## Explicit request cookies

The default CefSharp HTTP backend rejects `headers.Cookie` (case insensitive) and `details.cookie` before network dispatch with `MSP006_NOT_SUPPORTED`, even with `anonymous`. 84/121/151 native probes showed the supplied values never reached the server: URLRequest used jar values or no cookies when anonymous. String deduplication cannot repair that native behavior. No jar swapping, Cookie CRUD mutation or unverified network replacement is performed.

Ordinary jar-based login, TLS/Secure/HttpOnly/domain/path selection, redirects, anonymous suppression and independent RequestContexts retain their existing network validation. This limitation also applies to download headers through the same backend.

The [fixed Tampermonkey 5.5.1 reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-p0-cookies.json) gives `headers.Cookie` priority over the entire `details.cookie` string. Explicit names override jar names; repeated explicit names use the last value, preserving empty values and embedded `=`. An explicit string also collapses jar names across paths. Anonymous suppresses the jar but still sends explicit cookies, with headers taking priority. Custom verified `IHttpRequestService` backends can implement these observed merge rules; the default backend does not claim that behavior.

See the [native before/after evidence](../../scripts/compatibility/evidence/explicit-cookies-follow-up-p0.json).

## Evidence and reproduction

The [basic native matrix](../../scripts/compatibility/evidence/cookies-p0.json) covers real HTTP/TLS effects, SameSite, Secure, HttpOnly, host-only/domain cookies, expiration, exact multi-path deletion and two independent RequestContexts. The [pinned CRUD reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-cookie-returns.json) establishes return shapes and defaults.

The [partition matrix](../../scripts/compatibility/evidence/partition-cookies-p2.json) separately records CRUD and native request limitations. The [pinned partition reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-partition-cookies.json) retains its deletion side effect. The [final delivery snapshot](../../scripts/compatibility/evidence/p2-delivery.json) adds all three x64 WPF gates.

Run the [baseline and native gates](validation.md). SmokeHost's test certificate and DNS mapping are confined to the test host. Chromium 121's HTTP-loopback Secure behavior differs from 84, so TLS/credential claims use actual requests and readback, not mocks or CDP submission alone.
