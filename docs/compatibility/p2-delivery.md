# P2 delivery scope

Steps 16–19 were committed separately as `af3281f`, `b604555`, `d176090` and
`780b615`. Step 20 validates the resulting source in independent detached
worktrees, builds fresh NuGet versions, and tests a public-API consumer against
the packages rather than project references. The [final matrix](evidence/p2-delivery.json) passed all six combinations.

| Area | Delivered scope | Explicit boundary |
| --- | --- | --- |
| Historical webRequest | Official selectors/actions, capture replacement, scoped replacement/revocation, static first-request rules | MV2 reference unavailable; WebSocket interception, OOPIF and isolated dynamic page interception are not passed |
| Audio | Native mute state, callback/Promise calls, exact-function listeners, external host observations | Audible unavailable; external changes are polled; tested 151 navigation resets native mute |
| Private contexts | Real in-memory RequestContext, Cookie/cache/permission/tab-state isolation, temporary copied GM baseline, last-tab cleanup | WPF proof covers the delivered x64 versions; the existing page-world trust boundary remains |
| Partitioned Cookie | Verified 121/151 CRUD with canonical sites and full identity expiration/readback | 84 rejects; 121 ancestor field unavailable; opaque/nonce keys and request cookiePartition unavailable |

The pinned TM reference's partition delete removes the ordinary same-name
sibling. Its preservation assertion remains failed in the raw record.
MonkeySharp deliberately preserves that identity. This is a documented
difference, not a passing full-compatibility claim.

## Reproduction

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/compatibility/Run-Delivery.ps1 `
  -CorpusScript local-notes/Userscripts/about-blank-search.user.js
```

Each version/platform gets its own checkout, assets, outputs, profile and package
version. The runner records the base commit and SHA-256 of any tracked source
patch, stops on failure, and keeps failed worktrees for diagnosis. Successful
worktrees use short temporary paths on the workspace drive to stay within
net462 path limits, and are removed only after checking their resolved absolute paths. Reports
and package-consumer evidence remain under `artifacts/p2-delivery/<run>/`.
`-PackageOnly` is a diagnostic shortcut and is never counted as the full matrix.

The consumer checks actual CEF version/platform, Core AnyCPU architecture,
storage, ordinary cookies, native audio and partition capability. It also checks
that packages include compressed suffix source, provenance and third-party
notices. No higher minimum CEF or framework is introduced.

## Original local script intake

The existing `local-notes/Userscripts/about-blank-search.user.js` is installed
without editing its source. The consumer tests real DOM creation, engine choice
storage, empty submit validation, and full navigation retaining that choice.
It never submits a nonempty search or navigates to an external search engine.
The source stays outside commits because its redistribution license is not
declared. The manifest records the original file hash and local prerequisites.

This local script is not evidence for the seven missing pinned third-party
corpus categories. Those entries remain pending with their original intake
requirements. Historical MV2, full-GM strict document-start, transparent BFCache
restoration and OOPIF also retain the earlier documented limits.

Implementation evidence: [step records](p2-implementation.md),
[webRequest](evidence/webrequest-p2.json), [audio](evidence/audio-p2.json),
[private contexts](evidence/private-contexts-p2.json),
[partitioned cookies](evidence/partition-cookies-p2.json).

## Final validation

All six isolated builds, .NET/JS gates, 15-case SmokeHost/CDP suites, NuGet content checks, public consumers built only from NuGet packages, and original local script scenarios passed. Each x64 target passed 403 .NET tests and its WPF gate; each x86 target passed 357 .NET tests. JavaScript has 114 unique tests per run. WPF and private-context proof now includes 84, 121 and 151 x64.

Delivery validation corrected the NuGet source-data item declarations, centralized private-context construction across the removed 151 preference property, and read live native logs using shared file access. These fixes retain the basic 84 path, one bridge protocol, and existing API return categories. The prior reference hash was re-recorded from the committed LF fixture bytes; its failed preservation check remains failed.

Earlier failed validation directories are retained because automatic approval review rejected their cleanup command with only the reason "blocked by policy". They are not counted as passed runs. Successful final case checkouts were cleaned by the validated runner; their reports and packages remain available.
