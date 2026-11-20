# MonkeySharp documentation

**English** | [中文](README.zh-CN.md)

Start with the [repository README](../README.md) for installation and a minimal host.

| Audience | Guide |
| --- | --- |
| Userscript authors | [Userscript guide](usage-guide.md) |
| Host developers | [Integration reference](integration-reference.md) |
| Demo browser users | [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md) |
| Runtime contributors | [Development guide](development.md) |
| Users upgrading MonkeySharp | [Changelog](changelog.md) |

[Compatibility](compatibility/README.md) describes current capabilities, intentional differences, engine limits and the evidence supporting them, including [HTTP requests and streams](compatibility/network.md). [Validation](compatibility/validation.md) explains check selection, reproduction and the latest recorded matrix. The [tooling directory](../scripts/compatibility/README.md) owns runner parameters and inputs; the [evidence index](../scripts/compatibility/evidence/README.md) groups test baselines, capability matrices, reference observations, delivery snapshots and retained failures.

## Content ownership

Use the primary reference below for detailed rules. Other documents keep the context their readers need and link to that reference for the full contract.

| Question | Primary reference |
| --- | --- |
| What is MonkeySharp, and how do I start? | The [repository README](../README.md) covers packages, minimal integration and the security model. |
| How do I write or port a script? | The [userscript guide](usage-guide.md) covers metadata, grants, parameters, return values, errors and examples. |
| What must my host implement? | The [integration reference](integration-reference.md) covers configuration, service interfaces, persistence, ownership and lifecycle. |
| How do I operate or recover the Demo? | The [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md) covers the UI, deployment, portable data and Demo-specific policies. |
| How do I build, package or switch native versions? | The [development guide](development.md) covers the toolchain, source commands, packages and CI. |
| What changed between releases? | The [changelog](changelog.md) covers features, fixes and upgrade requirements by version. |
| Which behavior is reference-backed, different or limited? | The [reference baseline](compatibility/api-contract.md) and compatibility topics distinguish official descriptions, pinned observations and MonkeySharp guarantees. |
| Which checks should I run, and what do their results establish? | The [validation guide](compatibility/validation.md) covers check scope, result interpretation, triage and dated matrices. |
| What are the runner arguments and required inputs? | The [tooling reference](../scripts/compatibility/README.md) covers CLI parameters, pinned versions and fixtures. |
| Which recorded run supports a claim? | The [evidence index](../scripts/compatibility/evidence/README.md) lists test baselines, capability matrices, reference observations and historical deliveries. |

For example, script download parameters belong in the userscript guide, custom download-service obligations in the integration reference, compatibility differences in the download topic, and recorded outcomes in evidence. A link to Demo data recovery is useful for operating its profile; implementation guidance should also link directly to the relevant persistence code.

## What belongs in Git

Keep maintained guides, contracts, design constraints, reproducible fixtures and concise versioned evidence in Git. Update the English and Chinese guides together when behavior or commands change. Link to the topic that owns a behavior instead of repeating an implementation timeline.

Temporary plans, execution diaries, stage progress/delivery reports, intermediate investigations and full review worksheets belong in the ignored `local-notes/` directory. Promote their lasting conclusions into the maintained topic that owns each behavior before archiving the full record. Keep current capabilities and acceptance gaps in compatibility, source build/package commands in development, runner arguments in tooling, gate interpretation and recorded matrices in validation, and dated observations in versioned evidence. Older committed records remain available in Git history; a fresh checkout must not need local notes to understand the contract or run automated tests.

Raw logs, browser profiles, generated packages and transient reports belong in the ignored `artifacts/` directory. Keep compact evidence records with versions, source hashes, outcomes and limitations under `scripts/compatibility/evidence/`. Historical evidence describes the recorded run; reorganizing documentation does not turn a blocked or failed check into a pass.
