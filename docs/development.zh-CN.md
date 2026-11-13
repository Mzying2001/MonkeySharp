# MonkeySharp 开发指南

[English](development.md) | **中文**

本文集中说明仓库的构建、打包、验证与维护。宿主接入方式见[集成参考](integration-reference.zh-CN.md)。[验证指南](compatibility/validation.md)说明各门禁能证明的范围及已记录结果；[工具说明](../scripts/compatibility/README.md)维护 runner 参数与参考环境输入。

## 环境与门禁选择

清理并验证源码基线可运行 `./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10`（x64 包含 WPF）。完整原生/包消费门禁使用 `./scripts/compatibility/Run-Delivery.ps1`，隔离各版本/平台、核查包内容并运行公开 API 消费宿主。`-CorpusScript <path>` 可额外原样安装本地脚本，`-PackageOnly` 跳过基础门禁、仅供诊断。范围见[验证说明](compatibility/validation.md#reproduction)，参数见[工具说明](../scripts/compatibility/README.md)。

CI 在目标分支为 `dev` 或 `main` 的 PR 上运行，覆盖 CefSharp `84.4.10`、`156.0.150` 的 x64/x86，WPF 仅 x64。工具链为 Windows Server 2022、.NET SDK `9.0.315` 和 Node.js `24`，这些是已测试版本而非最低要求。SDK 须支持 `MonkeySharp.slnx`；Core 测试目标为 `net8.0`，CefSharp 测试/应用目标为 `net462`。真实 Chromium 门禁需要 Windows 桌面会话。

## 构建与测试

还原、构建、测试和打包必须使用相同的 `CefSharpVersion` 属性：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=156.0.150
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=156.0.150 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=156.0.150 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。不同原生版本或平台不能在同一个输出树中并发构建。

## 打包

使用 [Build-Packages.ps1](../scripts/Build-Packages.ps1) 构建发布包，需要 Windows PowerShell 5.1 或 PowerShell 7，以及 .NET SDK 8+：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Build-Packages.ps1 -Version 3.0.0

# 可选：指定一个平台、固定的新版引擎以及空输出目录。
./scripts/Build-Packages.ps1 -Version 3.0.1 -Platforms x64 -CurrentCefSharpVersion 156.0.150 -OutputDirectory artifacts/release
```

Windows 下可以双击 [Build-Packages.bat](../scripts/Build-Packages.bat) 按默认配置构建，也可以运行 `./scripts/Build-Packages.bat -Version 3.0.1 -Platforms x64 -OutputDirectory "artifacts/release packages"`。包装脚本不依赖当前工作目录，定位同目录的 PowerShell 脚本，通过 `-NoProfile -ExecutionPolicy Bypass` 绕过本次进程的执行限制，使用 `%*` 原样转发参数，并以 PowerShell 退出码作为自身结果。构建结束后直接退出，不暂停等待输入。

默认构建一个 `Mzying2001.MonkeySharp.Core` 包和四个适配包：面向 `84.4.10` 的 `Mzying2001.MonkeySharp.CefSharp84.x64`/`.x86`，以及面向固定新版 `156.0.150` 的 `Mzying2001.MonkeySharp.CefSharp.x64`/`.x86`。所有包使用相同的 MonkeySharp 版本。省略 `-Version` 或 `-CurrentCefSharpVersion` 时，脚本读取 Core 和适配器项目的默认值；不会动态解析最新版或执行发布。

脚本默认使用 Release 和两个平台，`-Configuration` 可选 Release 或 Debug。相对 `-OutputDirectory` 按仓库根目录解析，且必须为空；默认输出到新的 `artifacts/packages/<version>-<run>/` 目录。每次运行按引擎和平台将还原、构建产物隔离到 `artifacts/package-build/<run>/`，原生命令失败立即停止，并检查包标识、Core 目标框架、Core 传递依赖、CefSharp 精确版本依赖及符号包。`packages.json` 记录 `.nupkg` 和相应 `.snupkg` 的路径、版本、平台与 SHA-256。失败时保留部分产物用于诊断，仅完整成功后写入清单。生成包不会运行源码或原生门禁，发布前仍须验证；CI 在源码及真实 Chromium 作业通过后使用同一个脚本打包。

所有适配包保持相同的程序集与命名空间，每个应用程序只安装一种。Core 项目引用转换为 NuGet 依赖，`CefSharp.Common` 则是 `[84.4.10]` 等精确版本的公开依赖，宿主自行选择匹配的 WinForms/WPF 包。仅 `84.4.10` 在适配器包名中添加 `84`；手动构建其他新版并发布时，应使用新的 MonkeySharp 包版本。

检查并验证产物后，在 nuget.org 创建 API Key，先发布 Core，再发布所需适配包。例如，将路径替换为脚本实际输出目录：

```powershell
$packages = 'artifacts/packages/<version>-<run>'
dotnet nuget push "$packages/Mzying2001.MonkeySharp.Core.3.0.0.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
dotnet nuget push "$packages/Mzying2001.MonkeySharp.CefSharp84.x64.3.0.0.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
dotnet nuget push "$packages/Mzying2001.MonkeySharp.CefSharp.x64.3.0.0.nupkg" --source https://api.nuget.org/v3/index.json --api-key $env:NUGET_API_KEY
```

同时发布两个平台时，对 x86 重复发布。`dotnet nuget push` 也会推送相邻的符号包。已经发布的包名与版本组合无法覆盖，后续发布应使用新的 MonkeySharp 版本号。

手动打包时，Core 及不同平台适配器应输出到不同目录。切换引擎或平台时应使用独立输出树，或先清理再复用。覆盖项目版本时，Core 和适配器依赖的 `Version`、`PackageVersion` 应保持一致。不要通过全局 `PackageId` 参数覆盖适配器名称，该参数可能同时传递给被引用的 Core 项目。

Solution 的两个平台均将 Core 映射到 AnyCPU，产物位于 `bin/Release/<framework>/`。打包 Core 时应使用 `-p:Platform=AnyCPU`，通过 `--no-build` 复用 solution 产物时也应如此。CefSharp 适配器则需要显式指定 x64 或 x86 平台。

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=156.0.150 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=156.0.150 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=156.0.150 -o artifacts/packages/x86
```

## 真实 Chromium 门禁

在 Windows 桌面会话中运行真实 Chromium 验证：

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 156.0.150
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 156.0.150
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 156.0.150
```

验证全过程应使用同一版本值：`dotnet restore/build/test/pack` 使用 `-p:CefSharpVersion=<版本>`，PowerShell runner 使用 `-CefSharpVersion <版本>`。SmokeHost 应分别对 x64/x86 和 `84.4.10`、`156.0.150` 运行；Demo gate 覆盖两个版本的 x64，每个版本只运行一次默认英文界面。每份报告记录 `environment.uiLanguage`，并验证英文窗口及权限控件；其他语言由本地化单元测试覆盖，详见[界面语言和部署](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md#界面语言)。在已有工作区切换版本前，先执行 `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<目标版本>`（位数使用目标平台），避免混用不同 CefSharp 版本的增量构建产物。SmokeHost runner 已使用选定版本执行 restore、clean、build，并核对实际运行的程序集版本和进程位数。两个验证入口都使用隔离 profile 和 `TrustedPageWorld`；它们是集成测试，不是安全沙箱。

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
