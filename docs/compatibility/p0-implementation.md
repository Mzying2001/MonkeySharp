# P0 implementation record

Work follows the six P0 points of the local 2026-11-06 plan on
`refactor/tampermonkey-compatibility`, with one validated commit per point.

| Point | Result | Validation / remaining boundary |
| --- | --- | --- |
| 1. Contract/reference baseline | `001f578` | Pinned 5.5.1/MV3 return experiment; 84/x64 baseline. Third-party corpus intake and historical MV2 reference are explicitly pending. |
| 2. Browser protocol feasibility | `955b8b0` | Real SmokeHost/CDP at 84/121/151, x64/x86; 84/x64 .NET/JS/WPF. Full GM strict-start remains blocked by current host bootstrap; isolated privileged transport remains P1. |
| 3. Single contract and actual info | `45465c9` | Protocol 2; shared catalog; exact legacy names and reference-tested modern families; source strictness; modular JS; real manager/environment data. 74 JS tests, 231 Core tests, 37 adapter tests, 38 Demo tests; six real SmokeHost/CDP combinations and 84/121 x64 WPF. |
| 4. Downloads | `1454031` | Abortable completion Promise, subscribe-before-Start, shared CEF network and atomic files. 315 .NET / 81 JS tests; six real SmokeHost/CDP combinations; 84/121 x64 WPF including native picker cancellation. See [download evidence](download-findings.md). |
| 5. Notifications, menus, clipboard | Pending | Interaction completion, options and real WPF effects. |
| 6. Cookie backend | Pending | Shared CDP, precise selection, SameSite and real context isolation. |

Step 3 intentionally follows fixed runtime evidence where the original plan's
assumptions differ: `none` retains an info-only GM object; modern storage,
resources, menus and tab data are authorized as families; legacy global names
remain exact. The additional storage-authority probe verifies actual modern
write/read with only a legacy read declaration. Source declarations remain
unchanged in metadata and info; expanded internal operations are separate.

Raw validation logs remain in `artifacts/p0-step*-*.log`. Committed evidence
includes fixture hashes, original events and actual reference versions. A
passed API/bootstrap test is never substituted for a browser timing, network,
file or WPF effect that it did not exercise.
