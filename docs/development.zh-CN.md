# MonkeySharp 开发指南

[English](development.md) | **中文**

本文集中说明仓库的构建、打包、验证与维护。宿主接入方式见[集成参考](integration-reference.zh-CN.md)。[验证指南](compatibility/validation.md)说明各门禁能证明的范围及已记录结果；[工具说明](../scripts/compatibility/README.md)维护 runner 参数与参考环境输入。

## 环境与门禁选择

清理并验证源码基线可运行 `./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10`（x64 包含 WPF）。完整原生/包消费门禁使用 `./scripts/compatibility/Run-Delivery.ps1`，隔离各版本/平台、核查包内容并运行公开 API 消费宿主。`-CorpusScript <path>` 可额外原样安装本地脚本，`-PackageOnly` 跳过基础门禁、仅供诊断。范围见[验证说明](compatibility/validation.md#reproduction)，参数见[工具说明](../scripts/compatibility/README.md)。

CI 在目标分支为 `dev` 或 `main` 的 PR 上运行，覆盖 CefSharp `84.4.10`、`121.3.70`、`151.3.240` 的 x64/x86，WPF 仅 x64。工具链为 Windows Server 2022、.NET SDK `9.0.315` 和 Node.js `24`，这些是已测试版本而非最低要求。SDK 须支持 `MonkeySharp.slnx`；Core 测试目标为 `net8.0`，CefSharp 测试/应用目标为 `net462`。真实 Chromium 门禁需要 Windows 桌面会话。

## 构建与测试

还原、构建、测试和打包必须使用相同的 `CefSharpVersion` 属性：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。不同原生版本或平台不能在同一个输出树中并发构建。

## 打包

Core 及不同平台适配器应输出到不同目录：

Solution 的两个平台均将 Core 映射到 AnyCPU，产物位于 `bin/Release/<framework>/`。打包 Core 时应使用 `-p:Platform=AnyCPU`，通过 `--no-build` 复用 solution 产物时也应如此。CefSharp 适配器则需要显式指定 x64 或 x86 平台。

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=151.3.240 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=151.3.240 -o artifacts/packages/x86
```

## 真实 Chromium 门禁

在 Windows 桌面会话中运行真实 Chromium 验证：

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 151.3.240
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 151.3.240
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 151.3.240
```

验证全过程应使用同一版本值：`dotnet restore/build/test/pack` 使用 `-p:CefSharpVersion=<版本>`，PowerShell runner 使用 `-CefSharpVersion <版本>`。SmokeHost 应分别对 x64/x86 和 `84.4.10`、`121.3.70`、`151.3.240` 运行；Demo gate 覆盖三个版本的 x64，每个版本只运行一次默认英文界面。每份报告记录 `environment.uiLanguage`，并验证英文窗口及权限控件；其他语言由本地化单元测试覆盖，详见[界面语言和部署](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md#界面语言)。在已有工作区切换版本前，先执行 `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<目标版本>`（位数使用目标平台），避免混用不同 CefSharp 版本的增量构建产物。SmokeHost runner 已使用选定版本执行 restore、clean、build，并核对实际运行的程序集版本和进程位数。两个验证入口都使用隔离 profile 和 `TrustedPageWorld`；它们是集成测试，不是安全沙箱。

## Chromium 兼容性脚本包

SmokeHost 通过正常安装流程，在 loopback HTTP fixture 上运行仓库原创脚本。runner 要求全部 15 个兼容性场景，核验实际 CefSharp 版本、平台及清理，并将日志保存在 `artifacts/smoke/<version>-<platform>/`；可通过 `-OutputDirectory` 覆盖。

场景 ID、断言、超时、解析器检查及日志排查见 [SmokeHost 场景与结果验证](compatibility/validation.md#smokehost-cases-and-results)。[验证指南](compatibility/validation.md#latest-recorded-validation)维护已记录结果及剩余缺口，[工具说明](../scripts/compatibility/README.md)维护 runner 参数及固定参考环境前置条件。

## 原生生命周期维护

面向宿主的操作顺序及诊断预算见[集成参考](integration-reference.zh-CN.md#生命周期与诊断)。以下细节解释 [CefSharpUserScriptHost](../Mzying2001.MonkeySharp.CefSharp/CefSharpUserScriptHost.cs) 的原生 frame 恢复路径；[只读求值测试](../Mzying2001.MonkeySharp.CefSharp.IntegrationTests/ReadOnlyScriptEvaluatorTests.cs)与[原生 frame 测试](../Mzying2001.MonkeySharp.CefSharp.IntegrationTests/NativeFrameProbeTests.cs)验证相应失败边界。

同时运行页面和隔离世界的宿主通过异步页面标记探测区分文档导航与隔离 V8 上下文回调。旧版 CEF 可能复用 frame ID，并先完成释放探测、后完成创建探测。适配器在释放后保留帧代次编号，使待完成的创建探测仍可初始化新文档；后续文档、主页面替换或分离会使旧代次失效。这些记录不会保留已释放文档的能力或菜单注册。

CEF 84 在跨站 context-created 回调中还可能提供上一个进程的主帧包装。若加载完成事件标识了另一个从未初始化过、且与浏览器当前原生主帧一致的 frame，适配器会结束旧页面并初始化该帧，再推进 load 生命周期阶段。已在新页面创建的子帧会话仅在原生父帧链确认其属于新主帧时保留。此恢复不会重试已知失败或已释放的帧，也不提供 document-start 时序保证。

匹配前，适配器读取当前 renderer 的 `location.href`，因为旧版 CefSharp 的 context-created 回调可能仍携带上次导航 URL。只读求值取消后，在本次初始化的 15 秒总期限内重试；每次求值最多两秒，重试间隔为不超过剩余期限的 50 ms。调用方取消、分离、文档会话被替换或原生 frame 失效时停止初始化，不回退使用过期的回调 URL。耗尽求值期限会产生初始化诊断，其中包含 `TimeoutException` 和最后一次取消异常。读取失败时跳过该文档，包括 load fallback 及上下文菜单执行。document-start 仍为尽力执行。

## 文档维护

[文档索引](README.md#content-ownership)说明各主题的主参考。已有中英文指南应同步行为与命令，保留显式兼容锚点；检查链接是否能解析，也检查目标章节是否回答了链接承诺的问题。契约说明须与实现和测试一致，管理器对照还须对应固定版本证据。文档调整不更新历史验证日期或结果。
