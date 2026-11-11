# MonkeySharp documentation

Start with the repository [README](../README.md) / [中文说明](../README.zh-CN.md) for installation and a minimal host.

| Audience | English | 中文 |
| --- | --- | --- |
| Userscript authors | [Userscript guide](usage-guide.md) | [用户脚本使用指南](usage-guide.zh-CN.md) |
| Host developers | [Integration reference](integration-reference.md) | [集成参考](integration-reference.zh-CN.md) |

[Compatibility](compatibility/README.md) describes current capabilities, intentional differences, engine limits and the evidence supporting them, including [HTTP requests and streams](compatibility/network.md). [Validation](compatibility/validation.md) explains check selection, reproduction and the latest recorded matrix. The [tooling directory](../scripts/compatibility/README.md) owns runner parameters and inputs; the [evidence index](../scripts/compatibility/evidence/README.md) groups test baselines, capability matrices, reference observations, delivery snapshots and retained failures.

## What belongs in Git

Keep maintained guides, contracts, design constraints, reproducible fixtures and concise versioned evidence in Git. Update the English and Chinese guides together when behavior or commands change. Link to the topic that owns a behavior instead of repeating an implementation timeline.

Temporary plans, execution diaries, stage progress/delivery reports, intermediate investigations and full review worksheets belong in the ignored `local-notes/` directory. Promote their lasting conclusions into the maintained topic that owns each behavior before archiving the full record. Keep current capabilities and acceptance gaps in the compatibility index, commands and recorded matrices in validation, and dated observations in versioned evidence. Older committed records remain available in Git history; a fresh checkout must not need local notes to understand the contract or run automated tests.

Raw logs, browser profiles, generated packages and transient reports belong in the ignored `artifacts/` directory. Keep compact evidence records with versions, source hashes, outcomes and limitations under `scripts/compatibility/evidence/`. Historical evidence describes the recorded run; reorganizing documentation does not turn a blocked or failed check into a pass.
