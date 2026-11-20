# MonkeySharp 文档

[English](README.md) | **中文**

安装方式和最小宿主集成示例见[仓库说明](../README.zh-CN.md)。

| 读者 | 阅读入口 |
| --- | --- |
| 用户脚本作者 | [用户脚本使用指南](usage-guide.zh-CN.md) |
| 宿主开发者 | [集成参考](integration-reference.zh-CN.md) |
| Demo 浏览器用户 | [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md) |
| 运行时贡献者 | [开发指南](development.zh-CN.md) |
| 升级 MonkeySharp 的用户 | [更新日志](changelog.zh-CN.md) |

兼容性、验证和工具资料为英文。[兼容性说明](compatibility/README.md)记录当前能力、有意保留的差异、引擎限制及相关证据，其中包括 [HTTP 请求与流](compatibility/network.md)。[验证指南](compatibility/validation.md)说明如何选择和复现检查，并提供最近记录的验证矩阵。[工具说明](../scripts/compatibility/README.md)列出运行参数与输入。[证据索引](../scripts/compatibility/evidence/README.md)汇集测试基线、能力矩阵、参考实现观测、发布验证快照和保留的失败记录。

## 文档分工

各主题的详细规则见下表所列的主要文档。其他文档应提供读者所需的背景，并链接到相关章节以便查阅完整契约。

| 问题 | 主要文档 |
| --- | --- |
| MonkeySharp 是什么，如何开始使用？ | [仓库说明](../README.zh-CN.md)介绍软件包、最小宿主集成和安全模型。 |
| 如何编写或移植用户脚本？ | [用户脚本使用指南](usage-guide.zh-CN.md)介绍元数据、授权、参数、返回值、错误和示例。 |
| 宿主需要实现什么？ | [集成参考](integration-reference.zh-CN.md)说明配置、服务接口、持久化、资源归属和生命周期。 |
| 如何使用 Demo 或恢复其数据？ | [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md)介绍界面、部署、便携数据和 Demo 特有的行为规则。 |
| 如何构建、打包或切换原生组件版本？ | [开发指南](development.zh-CN.md)说明工具链、源码构建命令、打包和 CI。 |
| 各版本有哪些变化？ | [更新日志](changelog.zh-CN.md)按版本记录功能、修复和升级要求。 |
| 哪些行为有参考依据，哪些存在差异或限制？ | [参考基线](compatibility/api-contract.md)及各兼容性专题说明官方描述、固定版本的观测结果和 MonkeySharp 的行为保证。 |
| 应该运行哪些检查，结果能说明什么？ | [验证指南](compatibility/validation.md)说明检查范围、结果解读、故障排查和带日期的验证矩阵。 |
| 运行工具时需要哪些参数和输入？ | [工具说明](../scripts/compatibility/README.md)列出命令行参数、固定版本和测试材料。 |
| 哪条验证记录支持某项结论？ | [证据索引](../scripts/compatibility/evidence/README.md)列出测试基线、能力矩阵、参考实现观测和历史发布验证记录。 |

例如，脚本下载参数在用户脚本使用指南中说明，自定义下载服务的要求见集成参考，兼容性差异见下载专题，验证结果见证据记录。Demo 的使用说明可链接到数据恢复步骤，实现说明也应直接链接到相关持久化代码。

## 纳入 Git 的内容

将持续维护的指南、契约、设计约束、可复现的测试材料和精简的版本化证据纳入 Git。行为或命令有变化时，同步更新中英文指南，并链接到描述该行为的主要文档。

临时计划、执行日志、阶段进展或交付报告、中间调查和完整评审记录放在 Git 忽略的 `local-notes/` 目录。归档前，将长期有效的结论补充到对应文档。当前能力和验收缺口见兼容性说明，源码构建与打包命令见开发指南，运行参数见工具说明，检查结果的解读及矩阵见验证指南，带日期的观测见版本化证据。曾提交的旧记录可从 Git 历史查阅。从新检出的仓库即可理解契约并运行自动化测试。

原始日志、浏览器配置目录、生成的软件包和临时报告放在 Git 忽略的 `artifacts/` 目录。在 `scripts/compatibility/evidence/` 下保存精简的证据记录，包含版本、源码提交哈希、结果及限制。判断历史检查的阻塞或失败时，以当次证据记录为准。
