# MonkeySharp

[English](README.md) | **中文**

MonkeySharp 是面向 CefSharp 嵌入式应用的用户脚本运行时。Core 包含与浏览器无关的解析、URL 匹配、仓库、存储、权限和 API 分发；CefSharp 适配器负责浏览器生命周期集成。

脚本可以使用现代 `GM.*` 方法和旧式 `GM_*` 名称。可用能力由授权声明和宿主服务共同决定，调用约定见[用户脚本使用指南](docs/usage-guide.zh-CN.md#execution-grants-and-compatibility)。

| 目标 | 阅读入口 |
| --- | --- |
| 运行浏览器和脚本管理器 | [WPF 示例](#wpf-浏览器示例) |
| 在应用程序中集成 MonkeySharp | [最小集成](#最小宿主集成)，然后参阅[集成参考](docs/integration-reference.zh-CN.md) |
| 编写或移植用户脚本 | [用户脚本使用指南](docs/usage-guide.zh-CN.md) |
| 构建、打包或验证运行时 | [构建与测试](#构建与测试)，然后参阅[验证参考](docs/integration-reference.zh-CN.md#构建打包与验证) |

[文档导航](docs/README.md) · [兼容性范围与证据](docs/compatibility/README.md)

## 软件包

| 软件包 | 目标框架 | 用途 |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`、`netstandard2.0`、`net8.0` | 与浏览器无关的运行时和宿主契约。 |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | 64 位宿主的 CefSharp 生命周期适配器。 |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | 32 位宿主的同一适配器。 |

两个适配器包都公开 `Mzying2001.MonkeySharp.CefSharp` 程序集和命名空间。从源码集成时，可以直接引用适配器项目；Core 会被传递引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

宿主还必须引用版本匹配的 `CefSharp.WinForms`、`CefSharp.Wpf` 或 `CefSharp.Common` 包；适配器的 `CefSharp.Common` 依赖是私有的。应用程序和适配器必须使用同一个显式 `x64` 或 `x86` 进程平台。适配器默认使用 CefSharp `151.3.240`；发布前应针对实际版本编译和测试。已测试版本及绑定说明见[集成参考](docs/integration-reference.zh-CN.md#cefsharp-版本兼容性)。

## 快速开始

### WPF 浏览器示例

`Mzying2001.MonkeySharp.Demo` 是可运行的 `net462`/`x64` WPF 浏览器，包含持久化脚本、后台标签页、原生管理器、SQLite 存储和所支持宿主服务的实现。

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe
```

可执行文件目录必须可写，因为便携 profile 存储在其中的 `Data/` 目录。Demo 显式启用了 `TrustedPageWorld`：它是面向可信页面和脚本的集成示例，不是加固浏览器或完整的 Tampermonkey 替代品。使用方式、恢复流程、服务边界和验证方法见 [Demo 指南](Mzying2001.MonkeySharp.Demo/README.md)。

### 最小宿主集成

以下片段假设已有 Windows WinForms 应用程序，并已引用版本匹配的 `CefSharp.WinForms`。在进程启动时初始化 CefSharp 一次，且须早于任何浏览器的创建：

```csharp
CefSharp.CefSharpSettings.ConcurrentTaskExecution = true;
CefSharp.CefSharpSettings.WcfEnabled = false;
if (!CefSharp.Cef.Initialize(new CefSharp.WinForms.CefSettings()))
    throw new System.InvalidOperationException("CefSharp initialization failed.");
```

在 UI 线程的异步方法中安装脚本、构建宿主，并在将浏览器加入已有的 `form` 前附加宿主：

```csharp
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Repository;
using System.Threading;

var repository = new InMemoryUserScriptRepository();
await repository.InstallAsync(@"
// ==UserScript==
// @name Add a marker
// @match https://example.com/*
// @grant none
// @run-at document-end
// ==/UserScript==
document.documentElement.dataset.monkeySharp = 'ready';
", "application://bundled/marker.user.js", true, CancellationToken.None);

var host = new CefSharpUserScriptHostBuilder(repository).Build();
var browser = new CefSharp.WinForms.ChromiumWebBrowser("https://example.com/")
{
    Dock = System.Windows.Forms.DockStyle.Fill
};
host.Attach(browser);
form.Controls.Add(browser);
```

参数 `true` 表示启用该安装。页面加载后，在 DevTools 中执行 `document.documentElement.dataset.monkeySharp`，结果应为 `"ready"`。本例使用 `@grant none`。使用特权 API 时，按[安全模型](#安全模型)选择执行世界，并注册所需宿主服务。

在窗体存续期间保留宿主和浏览器。应先释放 MonkeySharp 宿主，再释放浏览器，并在全部浏览器释放后调用一次 `CefSharp.Cef.Shutdown()`。宿主重复释放是安全的，并会取消文档会话和待处理 API 请求。[SmokeHost](Mzying2001.MonkeySharp.CefSharp.SmokeHost/Program.cs) 提供了完整的 WinForms 启动和关闭示例。

### 已有的浏览器处理器

CefSharp 分别只提供一个渲染进程处理器槽位和一个请求处理器槽位。应用程序已使用其中任一槽位时，应在附加 MonkeySharp 前组合已有处理器：

```csharp
browser.RenderProcessMessageHandler =
    new RenderProcessMessageHandlerMultiplexer(applicationRenderHandler);

var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
requestHandlers.Add(applicationRequestHandler);
browser.RequestHandler = requestHandlers;
host.Attach(browser);
```

只需组合已有应用处理器的槽位。已被未复用处理器占用的槽位会使 `Attach` 拒绝操作。应在初始化前附加以覆盖首批资源；晚附加会明确标记，无法补拦截已发送请求。存在 webRequest 服务时，请求处理器槽位也有同样要求，默认服务亦不例外。禁用或替换网络服务的说明见[请求处理器集成](docs/integration-reference.zh-CN.md#web-request)。

## 安全模型

默认页面世界与显式 DOM 隔离世界具有不同的桥接要求：

| 执行方式 | 桥接要求 |
| --- | --- |
| 页面世界，无有效授权 / `none` / 仅 info | 无需桥接绑定或信任选项。 |
| 页面世界，包含已识别的 info 以外授权 | 需要 `TrustedPageWorld`，仅适用于可信页面和脚本。 |
| `@sandbox DOM` 或 `@inject-into content` | 使用每脚本隔离世界及通过原生 context 验证的 CDP 传输；仍需授权声明、服务及宿主许可。 |

页面世界绑定报告为 `Unverified`。每次执行的能力令牌不是浏览器扩展式隔离边界：恶意页面可以截获令牌并调用脚本已获授权的 API。可信页面世界应用接受该限制后，可显式启用：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

页面世界因缺少信任而跳过的执行产生 `MSR201_BRIDGE_INTEGRITY_REQUIRED`；可信页面世界中需要桥接的执行产生 `MSR200_UNVERIFIED_BRIDGE`。

已安装脚本属于可信代码，应用需要为未知来源制定信任策略。DOM 隔离分离脚本全局和派发身份，但不能暴露页面的 `unsafeWindow`；冲突要求和未支持的 unwrap 会拒绝。document-start 仍为尽力执行。OOPIF、菜单、CSP 和 BFCache 的范围见[执行世界与限制](docs/compatibility/execution-worlds.md)。

## 元数据

元数据涵盖脚本身份、URL 选择、执行时机、授权、依赖、资源及更新/来源地址。解析错误会阻止安装；警告保留在 `MetadataParseResult.Diagnostics` 中。支持字段、匹配规则和示例见[元数据指南](docs/usage-guide.zh-CN.md#元数据)。

`@require` 和 `@resource` 由仓库的 `UserScriptPreparer` 在安装或更新激活前获取并校验。执行时跨 frame 复用已准备内容，支持离线重复执行。Demo 持久化这些版本并提供手动更新。摘要语法、预算和更新规则见[集成参考](docs/integration-reference.zh-CN.md#资源完整性与更新检查)。

## GM API 支持

`GM.info` 和 `GM_info` 无需授权或桥接，始终可用。其他 GM API 需要所属家族授权及对应宿主服务；旧式 `GM_*` 全局名称需要精确声明。

下表概述可用能力。参数、返回值、回调及 [Tampermonkey 差异](docs/usage-guide.zh-CN.md#tampermonkey-兼容性检查表)详见[用户脚本使用指南](docs/usage-guide.zh-CN.md#api-索引)。

| API | 可用性 | 说明 |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | 共用不可变的安装信息，无需授权或桥接，默认可用。 |
| `GM.log` / `GM_log` | Core | 本地 console 输出及可选宿主日志回调。 |
| 值存储与变更监听 | Core | 按安装实例隔离的类型化单键/批量存储；宿主提供持久化存储时可跨重启保留。 |
| `GM.addStyle`、`GM.addElement` 及别名 | 引导代码 | 页面本地实现。 |
| `GM.getResourceText`、`GM.getResourceUrl`、`GM_getResourceText`、`GM_getResourceURL` | 支持 | 消费安装时已准备的声明资源；旧式名称需要精确 grant。 |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp 默认提供 | 需要 `@connect`；支持凭据、中止、增量文本及二进制/multipart/暂存流请求体。详见 [HTTP 请求](docs/usage-guide.zh-CN.md#http-请求)。 |
| `GM.registerMenuCommand`、`GM.unregisterMenuCommand` 及别名 | 条件支持 | 需要 `IMenuService`。 |
| `GM.notification`、`GM.setClipboard`、`GM.openInTab` 及别名 | 条件支持 | 需要对应宿主服务。 |
| `GM.download` / `GM_download` | 条件支持 | 需要 `IDownloadService`，文件提交后才完成；Demo 共用浏览器网络上下文。详见[下载](docs/usage-guide.zh-CN.md#下载)。 |
| `GM.getTab`、`GM.saveTab`、`GM.getTabs` 及别名 | 条件支持 | 需要 `ITabStateService`。 |
| `GM.cookie` / `GM_cookie` | CefSharp 默认提供 | 实际 RequestContext 中的 CRUD；121/151 支持分区 CRUD，84 不支持。不提供变更监听或 XHR 请求分区选择。 |
| `GM.audio` / `GM_audio` | CefSharp 默认提供 | 原生静音状态、操作及监听；省略 audible，并明确报告部分能力。 |
| `GM.webRequest` / `GM_webRequest` | CefSharp 默认提供 | 有归属的取消/重定向规则、捕获替换、异步结果回调及静态元数据规则；无自有移除句柄。 |
| `window.close`、`window.focus` | 条件支持 | 需要各自的精确授权和 `IUserScriptWindowService`；宿主可以拒绝关闭最后一个标签页。 |
| `window.onurlchange` | Core | 需要精确授权；监听 `pushState`、`replaceState`、`popstate` 和 `hashchange` URL 变化。 |
| `unsafeWindow` | 页面世界 | 除 `@grant none` 外指向页面全局；DOM 隔离世界不可用。详见 [window 扩展](docs/usage-guide.zh-CN.md#window-extensions)。 |

旧式同步存储/资源 API 使用完整启动快照。返回值和错误渠道因 API 而异，详见 [API 索引](docs/usage-guide.zh-CN.md#api-索引)及[错误处理](docs/usage-guide.zh-CN.md#错误与排查)。

缺少提供程序时，已声明 API 通常不会公开，直接桥接调用返回 `MSP006_NOT_SUPPORTED`；未声明的方法返回 `MSP004_GRANT_DENIED`。

## 宿主服务

Builder 默认使用内存值存储。通过 `UseValueStore`、`UseMenuService` 等方法注册应用服务，提供持久化及其他能力。

默认 XHR、Cookie 和 webRequest 服务使用已附加浏览器的请求上下文。可通过 `UseHttpRequestService`、`UseCookieService`、`UseWebRequestService` 逐项替换，或设置 `EnableDefaultNetworkServices = false` 省略全部三个默认服务。

完整注册示例、所有权规则及提供程序契约见[宿主服务参考](docs/integration-reference.zh-CN.md#宿主服务契约)，其中也说明了 `AddApiProvider` 的所有权转移。

## 生命周期与诊断

脚本在每个匹配文档/框架/生命周期阶段执行一次；`@noframes` 禁止子框架执行。`@run-at context-menu` 仅在原生菜单选择后执行。导航、上下文释放、分离及宿主释放撤销旧执行和待处理 API 调用；已接受的下载可继续到完成或宿主释放。

同文档导航不会重新注入脚本。SPA 更新请使用已授权的 `window.onurlchange` API 或 DOM 观察器，详见[匹配与生命周期](docs/usage-guide.zh-CN.md#匹配与生命周期)。

CefSharp 只能提供尽力而为的 document-start 注入。此类执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

应订阅结构化诊断：

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

协议错误码和桥接限制见[集成参考](docs/integration-reference.zh-CN.md#生命周期与诊断)。

## 构建与测试

在 Windows 上使用支持 `MonkeySharp.slnx` 的 .NET SDK 构建，并安装 Node.js 运行 JavaScript 测试。CI 使用 .NET SDK `9.0.315` 和 Node.js `24`；这些是已测试版本，不代表最低要求。Core 测试目标为 `net8.0`，CefSharp 测试及应用程序目标为 `net462`。

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。测试其他受支持版本时，应向所有命令传入相同的 `CefSharpVersion` 属性。打包和真实 Chromium E2E 命令见[集成参考](docs/integration-reference.zh-CN.md#构建打包与验证)。

CI 在 x64/x86 上覆盖 CefSharp `84.4.10`、`121.3.70` 和 `151.3.240`，运行 .NET/JavaScript 测试及真实 Chromium SmokeHost 门禁。WPF Demo 在三个版本的 x64 上设门禁。见[兼容性门禁详情](docs/integration-reference.zh-CN.md#chromium-兼容性脚本包)及 [验证说明](docs/compatibility/validation.md)。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
