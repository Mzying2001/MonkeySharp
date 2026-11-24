# 参与 MonkeySharp 贡献

[English](CONTRIBUTING.md) | **中文**

## 提交 Issue

先搜索已有 Issue。报告 MonkeySharp 的异常行为请选择**缺陷报告**，对比脚本管理器或 CefSharp 环境间的行为请选择**兼容性报告**，提出改进请选择**功能建议**。[新建 Issue](https://github.com/Mzying2001/MonkeySharp/issues/new/choose)

请提供能复现问题的最短步骤，并在有条件时附上最小用户脚本或宿主示例。用户脚本应保留影响复现的元数据和页面条件。分享示例或日志前，请移除其中的凭据和私人数据。

## 提交 Pull Request

1. Fork 仓库，从 `dev` 创建工作分支，提交 PR 时以 `dev` 为目标分支。维护者指定分支时，以指定分支为准。
2. 让 PR 聚焦于同一项改动。涉及行为变化时，同步更新相关测试和文档。修改已有的中英文配套指南时，保持两版内容一致。[文档导航](docs/README.zh-CN.md)
3. 使用 Conventional Commits，提交摘要用简洁的英文，例如 `fix(core): handle missing resource metadata`。非琐碎改动应在提交正文说明原因和做法。
4. 按 PR 模板填写改动原因、相关 Issue、实际验证结果，以及适用的兼容性或合并风险。

## 按改动选择验证

- 修改 Core 时运行相关 .NET 测试。修改 JavaScript 桥接或 GM API 时运行相关 JavaScript 测试。[构建与测试](docs/development.zh-CN.md#构建与测试)
- 修改 CefSharp 适配器时显式指定 `x64` 或 `x86`，并让各项目和检查使用同一个 CefSharp 精确版本。WPF Demo 及其测试使用 `x64`。涉及浏览器行为时，从 Windows 桌面会话运行相应的真实 Chromium 验证。[真实 Chromium 门禁](docs/development.zh-CN.md#真实-chromium-门禁)
- 仅修改文档时，检查链接和锚点。涉及中英文配套指南时，对照两版说明。[文档维护](docs/development.zh-CN.md#文档维护)

在 PR 中记录实际执行的检查和结果。如果有相关检查无法运行，请说明原因。
