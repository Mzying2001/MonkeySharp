# MonkeySharp documentation

Start with the repository [README](../README.md) / [中文说明](../README.zh-CN.md) for installation and a minimal host.

| Audience | English | 中文 |
| --- | --- | --- |
| Userscript authors | [Userscript guide](usage-guide.md) | [用户脚本使用指南](usage-guide.zh-CN.md) |
| Host developers | [Integration reference](integration-reference.md) | [集成参考](integration-reference.zh-CN.md) |
| Demo browser users | [Demo guide](../Mzying2001.MonkeySharp.Demo/README.md) | [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md) |
| Runtime contributors | [Development guide](development.md) | [开发指南](development.zh-CN.md) |

[Compatibility](compatibility/README.md) describes current capabilities, intentional differences, engine limits and the evidence supporting them, including [HTTP requests and streams](compatibility/network.md). [Validation](compatibility/validation.md) explains check selection, reproduction and the latest recorded matrix. The [tooling directory](../scripts/compatibility/README.md) owns runner parameters and inputs; the [evidence index](../scripts/compatibility/evidence/README.md) groups test baselines, capability matrices, reference observations, delivery snapshots and retained failures.

## Content ownership

Use the primary reference below for detailed rules. Other documents keep the context their readers need and link to that reference for the full contract.

| Question | Primary reference |
| --- | --- |
| What is MonkeySharp, and how do I start? | Repository [README](../README.md) / [中文](../README.zh-CN.md): packages, minimal integration and security overview. |
| How do I write or port a script? | Userscript guide: metadata, grants, parameters, returns, errors and examples. |
| What must my host implement? | Integration reference: configuration, service interfaces, persistence, ownership and lifecycle. |
| How do I operate or recover the Demo? | Demo guide: UI, deployment, portable data and Demo-specific policies. |
| How do I build, package or switch native versions? | Development guide: toolchain, source commands, packages and CI. |
| Which behavior is reference-backed, different or limited? | [Reference baseline](compatibility/api-contract.md) and compatibility topics: separate official descriptions, pinned observations and MonkeySharp guarantees. |
| Which checks should I run, and what do their results establish? | [Validation](compatibility/validation.md): gate scope, result interpretation, triage and dated matrices. |
| What are the runner arguments and required inputs? | [Tooling reference](../scripts/compatibility/README.md): CLI parameters, pins and fixtures. |
| Which recorded run supports a claim? | [Evidence index](../scripts/compatibility/evidence/README.md): test baselines, capability matrices, reference observations and historical deliveries. |

For example, script download parameters belong in the userscript guide, custom download-service obligations in the integration reference, compatibility differences in the download topic, and recorded outcomes in evidence. A link to Demo data recovery is useful for operating its profile; implementation guidance should also link directly to the relevant persistence code.

## What belongs in Git

Keep maintained guides, contracts, design constraints, reproducible fixtures and concise versioned evidence in Git. Update the English and Chinese guides together when behavior or commands change. Link to the topic that owns a behavior instead of repeating an implementation timeline.

Temporary plans, execution diaries, stage progress/delivery reports, intermediate investigations and full review worksheets belong in the ignored `local-notes/` directory. Promote their lasting conclusions into the maintained topic that owns each behavior before archiving the full record. Keep current capabilities and acceptance gaps in compatibility, source build/package commands in development, runner arguments in tooling, gate interpretation and recorded matrices in validation, and dated observations in versioned evidence. Older committed records remain available in Git history; a fresh checkout must not need local notes to understand the contract or run automated tests.

Raw logs, browser profiles, generated packages and transient reports belong in the ignored `artifacts/` directory. Keep compact evidence records with versions, source hashes, outcomes and limitations under `scripts/compatibility/evidence/`. Historical evidence describes the recorded run; reorganizing documentation does not turn a blocked or failed check into a pass.
