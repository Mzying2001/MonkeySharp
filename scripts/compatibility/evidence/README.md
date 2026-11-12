# Compatibility evidence index

These versioned records preserve observed versions, inputs, commits, ordered events, effects and limitations. Current behavior is maintained in [compatibility topics](../../../docs/compatibility/README.md); [validation](../../../docs/compatibility/validation.md) owns check selection and recorded delivery scope. Records use capability names; keep test and cross-record references in sync when renaming them. A passed assessment does not establish an implemented capability. Failed, blocked and superseded results retain their original status.

The [differential schema](schema.json) applies to reference records; native matrices and delivery summaries retain their own shapes. Raw logs, profiles and packages belong under ignored `artifacts/`. Artifact paths and historical fixture hashes describe the original run, so they need not match a later fixture revision or exist in a fresh checkout. Historical artifact paths and observations retain their original names; maintained record identifiers, field names and descriptions use capability terminology.

## Test reference baselines

These records are read directly by Core or JavaScript tests. Their contents are shared test inputs; update consumers when renaming records.

| Test scope | Records |
| --- | --- |
| Grant exposure, TM 5.5.1 | [missing](tampermonkey-5.5.1-grants-missing.json), [none](tampermonkey-5.5.1-grants-none.json), [legacy](tampermonkey-5.5.1-grants-legacy.json), [modern](tampermonkey-5.5.1-grants-modern.json), [mixed](tampermonkey-5.5.1-grants-mixed.json), [unknown](tampermonkey-5.5.1-grants-unknown.json), [info-only](tampermonkey-5.5.1-grants-info-only.json) |
| Lifecycle replay, TM 5.5.1 | [document-end](tampermonkey-5.5.1-lifecycle-end.json), [document-idle](tampermonkey-5.5.1-lifecycle-idle.json) |
| Matching, TM 5.5.1; embedded by Core tests | [matching conditions](tampermonkey-5.5.1-matching-conditions.json) |
| Storage listener order, TM 5.5.1 | [storage timing](tampermonkey-5.5.1-storage-timing.json) |

## Capability matrices

Each matrix covers its recorded engines/platforms and implementation. Consult its runs and limitations before applying a result to another configuration; newer topic evidence can extend an older matrix's scope.

| Topic | Records and scope |
| --- | --- |
| Native CDP feasibility | [84.4.10](cdp-84.4.10.json), [121.3.70](cdp-121.3.70.json), [151.3.240](cdp-151.3.240.json); registration and protocol effects are separate from full-GM timing |
| Resource preparation and storage | [prepared revisions](prepared-revisions.json), [atomic snapshots](atomic-storage.json), [storage mirror](storage-mirror.json) |
| Matching and permissions | [execution conditions](execution-conditions.json), [permission decisions](permissions.json) |
| Execution worlds and page services | [world/lifecycle matrix](execution-worlds.json), [page/tab helpers](page-helpers.json), [URL listener follow-up](urlchange-follow-up.json), [DOM failure follow-up](dom-follow-up.json) |
| Cookies and credentials | [basic CRUD](cookies.json), [partition CRUD](partition-cookies.json), [explicit Cookie limitation](explicit-cookies-follow-up.json); request partitions remain unsupported |
| Downloads and binary transport | [download effects](downloads.json), [binary inputs](binary-transport.json), [download error follow-up](download-docs-follow-up.json) |
| Network enhancements | [incremental response text](progress-follow-up.json), [staged stream input](stream-body-follow-up.json); fixed-beta differences remain explicit |
| Host interactions | [native/WPF matrix](interactions.json), [menu input/frame follow-up](menus-follow-up.json); full menu acceptance remains incomplete |
| Historical requests, audio and privacy | [webRequest](webrequest.json), [mute-only audio](audio.json), [private contexts](private-contexts.json) |
| Isolated child targets | [OOPIF routing](oopif-follow-up.json); source/storage/resources/XHR scope, with separate menu/start/BFCache limits |
| Feasibility assessments | [BFCache](bfcache-follow-up.json), [full-GM start and CSP](start-csp-follow-up.json); restoration, strict start and CSP bypass are not implemented |

## Fixed reference observations

Stable Tampermonkey 5.5.1/MV3 and beta 5.6.6242/MV3 are separate pinned environments. Browser versions, settings and identity qualification belong to each record. The modern extension's Chromium minimum is independent of MonkeySharp's CefSharp 84 baseline. See [pin and acquisition instructions](../README.md#pinned-reference).

| Reference scope | Records and boundary |
| --- | --- |
| TM 5.5.1 return shapes and receiver | [return inventory](tampermonkey-5.5.1-returns.json), [raw receiver](tampermonkey-5.5.1-raw-receiver.json) |
| TM 5.5.1 modern family grants | [clipboard](tampermonkey-5.5.1-group-clipboard.json), [download](tampermonkey-5.5.1-group-download.json), [log](tampermonkey-5.5.1-group-log.json), [menus](tampermonkey-5.5.1-group-menus.json), [notification](tampermonkey-5.5.1-group-notification.json), [resources](tampermonkey-5.5.1-group-resources.json), [tabs](tampermonkey-5.5.1-group-tabs.json) |
| TM 5.5.1 resources and storage authority | [prepared content](tampermonkey-5.5.1-prepared-content.json), [storage authority](tampermonkey-5.5.1-storage-authority.json) |
| TM 5.5.1 URL matching | [all URLs](tampermonkey-5.5.1-matching-all-urls.json), [default matching](tampermonkey-5.5.1-matching-default.json); the complete conditions record is a test baseline above |
| TM 5.5.1 DOM and page helpers | [DOM failures](tampermonkey-5.5.1-dom.json), [page/tab effects](tampermonkey-5.5.1-page-helpers.json) |
| TM 5.5.1 Cookie behavior | [CRUD returns](tampermonkey-5.5.1-cookie-returns.json), [explicit merge rules](tampermonkey-5.5.1-cookies.json), [partition deletion](tampermonkey-5.5.1-partition-cookies.json); sibling-preservation assertion remains failed |
| TM 5.5.1 URL and notifications | [URL payload/options](tampermonkey-5.5.1-urlchange.json), [notification replacement](tampermonkey-5.5.1-notification.json); native notification interactions still need comparisons |
| TM 5.5.1 binary, audio and privacy | [binary inputs](tampermonkey-5.5.1-binary-inputs.json), [audio](tampermonkey-5.5.1-audio.json), [temporary private tabs](tampermonkey-5.5.1-private-tabs.json) |
| TM 5.5.1 native downloads | [download probe](tampermonkey-5.5.1-download-errors.json); blocked by browser interaction, without a terminal result |
| TM 5.6.6242 incremental text | [canonical unpacked reference](tampermonkey-5.6.6242-progress-repro.json); fetch progress differs from MonkeySharp |
| TM 5.6.6242 stream body | [HTTP/2 bounded observation](tampermonkey-5.6.6242-stream-body-observed.json); reader release, redirect replay and error settlement differ |

## Corpus results

Pinned sources, licenses and seven functional categories are described in [corpus scope](../corpus/README.md). Intake readiness, native runtime results and fixed-manager comparisons answer different questions.

| Record | Result boundary |
| --- | --- |
| [Corpus follow-up](corpus-follow-up.json) | Intake/runner delivered; full reference acceptance remains incomplete |
| [Stable reference corpus](corpus-reference-stable.json) | Saved-file/archive/cancellation permission blocked; history-refresh assertion failed |
| [Native 121](corpus-native-121.json), [native 151](corpus-native-151.json) | Recorded functional scope, with start/CSP limits |
| [Initial native 84](corpus-native-84-initial.json) | Original failures include missing replaceAll; later classification accepts only the exact observed engine restriction |

## Delivery snapshots

| Snapshot | Implementation and acceptance |
| --- | --- |
| [Latest enhanced delivery, 2026-11-11](enhanced-follow-up-delivery.json) | Implementation 1ef4c4f; six native/package combinations and three x64 WPF gates passed selected scope. Full stable-reference acceptance and menu acceptance remain incomplete. |
| [Stable scoped delivery, 2026-11-11](stable-follow-up-delivery.json) | Implementation fe3c8df; historical stable scope. Full stable-reference acceptance and comprehensive acceptance remain incomplete. |
| [Original delivery, 2026-11-07](delivery.json) | Historical six-combination result with original counts and limits; does not establish complete compatibility. |

Delivery records retain earlier failures, superseded matrices, source-patch identity and original artifact locations. The [validation guide](../../../docs/compatibility/validation.md#latest-recorded-validation) summarizes the latest recorded matrix.

## Retained failures and regressions

These records remain part of the evidence chain. A successful correction or later run does not relabel an earlier failure, timeout or invalidated attempt as passed.

| Investigation | Retained records |
| --- | --- |
| URL AbortSignal assumption | [initial failed stable URL probe](tampermonkey-5.5.1-urlchange-initial-failed.json) |
| Beta publisher identity and fetch assumption | [initial failed progress probe](tampermonkey-5.6.6242-progress-initial-failed.json), [corrected publisher run before canonical unpacking](tampermonkey-5.6.6242-progress.json); the initial run is not identity-qualified reference evidence |
| Beta stream acquisition/transport/assertions | [initial stream probe](tampermonkey-5.6.6242-stream-body.json), [diagnostic attempt](tampermonkey-5.6.6242-stream-body-diagnostic.json), [second diagnostic](tampermonkey-5.6.6242-stream-body-diagnostic2.json), [HTTP/2 attempt](tampermonkey-5.6.6242-stream-body-http2.json); all retain failed status |
| WPF polling across reload | [polling regression and reruns](wpf-polling-follow-up.json) |
| Corpus host Cookie readiness | [targeted probes, full rerun and earlier timeouts](corpus-host-readiness.json); corrected readiness does not establish every native timeout cause |
| Enhanced public consumer | [package-only pilot](enhanced-consumer-pilot.json); diagnostic, separate from the final delivery |

Other retained attempts are embedded in their capability/delivery records, including native OOPIF retirement diagnostics, BFCache and start/CSP assessment corrections. Historical notes and full implementation diaries belong in ignored `local-notes/`; reproducible evidence remains tracked here.
