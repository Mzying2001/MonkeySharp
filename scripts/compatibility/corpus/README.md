# Pinned third-party corpus

`source.user.js` files are unmodified upstream bytes, including metadata and line endings. Each source and license is hashed in [corpus.json](../corpus.json). Both scripts declare MIT licensing; the original full licenses are included. Git treats these pinned sources/licenses as binary so checkout and delivery patches preserve bytes. Delivery installs changed attributes before other files. No runtime dependencies are fetched. The corpus does not modify `@match`, `@connect`, grant or failed source branches, and does not polyfill Chromium 84.

| Category | Upstream script | Functional check |
| --- | --- | --- |
| DOM decoration | SiteAssetDL 1.0.0 | Shadow toolbar, computed style, no duplicate after SPA; separate style-src CSP case |
| Page hook | SiteAssetDL 1.0.0 | History wrapper, changed asset collection; first inline canary recorded separately |
| Authenticated XHR | SiteAssetDL 1.0.0 | HEAD/GET through cross-origin port redirect with fake jar login; downloaded ZIP contains exact PNG bytes |
| Download | SiteAssetDL 1.0.0 | Saved PNG bytes/hash; separate real cancel transition with no file |
| Storage listener | Find Scripts For This Site 0.4.3 | Two independent host/browser instances, local and remote menu update from stored settings, persistence across navigation |
| Menu configuration | Find Scripts For This Site 0.4.3 | Actual host menu callback opens settings; save/read, replacement/unregister, same-process child-frame subscription |
| Iframe/SPA | SiteAssetDL 1.0.0 | Same-process frame decoration, URL change and single toolbar; OOPIF/BFCache explicitly excluded |

An additional MonkeySharp policy-denial case checks script failure UI, no saved file and no GM network requests after a phase marker. Its host policy is explicit; it is not a Tampermonkey permission comparison. Cancel behavior includes the upstream script's own fallback and does not imply that every host task shares the same owner. The private-context gates remain in SmokeHost/WPF.

The server listens on two randomly assigned loopback ports, preserving the upstream scheme/host/match/connect facts. Port-changing redirects are cross origin. Only `corpus-auth=fixture-only` is seeded; no personal sessions, hosts-file changes or system certificates are used. CSP uses an actual response header.

The host is shared between SmokeHost and the package consumer and uses only public MonkeySharp services. WinForms `ToolStripMenuItem.PerformClick` invokes the native host menu callback; it is not a claim of trusted DOM user activation. Tampermonkey uses its action UI for the exact originating tab. Only completed actions are included in events; failed actions and reasons remain in results.

Chromium 84 lacks `String.prototype.replaceAll`, used by the unmodified Find Scripts settings panel. That exact observed TypeError plus engine version and feature check classifies these two scenarios as `expected-engine-limitation`. No generic test failure is relabeled as an engine restriction.

See [CLI and output](../README.md#pinned-third-party-corpus) and [recorded corpus results](../evidence/README.md#corpus-results). A reference without browser downloads permission is blocked, not passed. Full stable-reference acceptance still needs saved-file/ZIP/cancellation and native notification/download interaction comparisons, plus resolution of the failed reference history-refresh assertion; see [current evidence gaps](../../../docs/compatibility/README.md#known-evidence-gaps).
