# Execution worlds and lifecycle evidence

The adapter now executes `@sandbox DOM` / `@inject-into content` in per-installation, per-revision isolated worlds. Native CDP frame/loader/context events establish ownership. Source and delivery use a remote global-object ID tied to a context generation. Binding packets must belong to that native context's script and document before reaching the normal grant/permission gateway; the page binding rejects isolated proofs. CEF frame IDs are not cast into CDP IDs.

Raw/default and Chromium JavaScript access remain in the page world. Host-backed raw privileges require `TrustedPageWorld`; DOM dispatch uses verified native context ownership. DOM plus unsafeWindow, unsupported world requirements and unwrap reject before user source. Main-frame native manual selection can dispatch either world. CEF's additional context callbacks caused by isolated V8 worlds are coalesced on the explicitly trusted raw path; raw scripts execute once, and isolated authentication does not use the page-world deduplication marker.

The fixed Tampermonkey 5.5.1 references show private window-event replay: [document-end](evidence/tampermonkey-5.5.1-lifecycle-end.json) receives DOMContentLoaded/load after registration, while [idle](evidence/tampermonkey-5.5.1-lifecycle-idle.json) receives load. Replayed events are synthetic (`isTrusted: false`), and direct document listeners receive no private replay. The same original fixture bodies pass the JS facade tests. Raw and isolated browser fixtures delay the host snapshot while the DOM completes, then verify cached DOMContentLoaded delivery. Idle runs after DOMContentLoaded while a slow image still prevents load.

The [browser matrix](evidence/execution-worlds-p1.json) separately records source, DOM and style CSP effects. On the tested CSP fixture, isolated source and DOM insertion succeed, inserted styles remain blocked, page inline script remains blocked and raw Function-based source execution is blocked. The adapter never calls a whole-page CSP bypass. A node being returned does not prove its style applied.

Validated combinations include full synchronous value/resource readiness, actual XHR, separate A/B globals, an invisible page canary, native proof-scope rejection, same-process child frames, explicit about:blank/srcdoc matching, SPA URL events and navigation revocation. Raw control scripts execute once alongside isolated worlds. Context-created/released counts balance in the real gate. See also the [focused security review](execution-security-review.md).

Remaining limits are explicit:

- Full-GM document-start remains best effort, including prepared navigation. `RequireGuaranteedDocumentStart` rejects these combinations; protocol registration is not proof of pre-inline user execution.
- OOPIF child-target routing is not implemented. Failed native world creation reports an error and uses no raw fallback.
- BFCache cached executions revoke their GM/window state and require a fresh navigation; transparent restoration is not claimed.
- Isolated child-frame manual menus are not mapped to native CEF menu frames. Main-frame selection is supported.
- Historical page-frame webRequest interception is unavailable from isolated contexts. P2 owns that feature's frame mapping and rule contract.
- User-managed DOM listeners/timers are not an unload API for arbitrary code in a live document; document navigation destroys normal contexts, and runtime-owned listeners/bridge state are revoked explicitly.
