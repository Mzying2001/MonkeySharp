# MonkeySharp 3.0

[English](README.md) | **中文**

MonkeySharp 3 是面向 CefSharp 嵌入式应用的用户脚本运行时。Core 包含与浏览器无关的解析、URL 匹配、仓库、存储、权限和 API 分发；CefSharp 适配器负责浏览器生命周期集成。

单一契约通过异步桥接提供两种官方调用形式；旧式名称遵循精确声明，现代 API 家族按固定版本实测和真实宿主服务开放。

| 目标 | 阅读入口 |
| --- | --- |
| 运行浏览器和脚本管理器 | [WPF 示例](#wpf-浏览器示例) |
| 在应用程序中集成 MonkeySharp | [最小集成](#最小宿主集成)，然后参阅[集成参考](docs/integration-reference.zh-CN.md) |
| 编写或移植用户脚本 | [用户脚本使用指南](docs/usage-guide.zh-CN.md) |
| 构建、打包或验证运行时 | [构建与测试](#构建与测试)，然后参阅[验证参考](docs/integration-reference.zh-CN.md#构建打包与验证) |

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

宿主还必须引用版本匹配的 `CefSharp.WinForms`、`CefSharp.Wpf` 或 `CefSharp.Common` 包；适配器的 `CefSharp.Common` 依赖是私有的。应用程序和适配器必须使用同一个显式 `x64` 或 `x86` 进程平台。适配器默认使用 CefSharp `121.3.70`；发布前应针对实际版本编译和测试。已测试版本及绑定说明见[集成参考](docs/integration-reference.zh-CN.md#cefsharp-版本兼容性)。

## 快速开始

### WPF 浏览器示例

`Mzying2001.MonkeySharp.Demo` 是可运行的 `net462`/`x64` WPF 浏览器，包含持久化脚本、后台标签页、原生管理器、SQLite 存储和所支持宿主服务的实现。

私密标签使用独立内存上下文、临时权限和普通 GM 初值副本，私密写入不会回流普通存储。见[私密浏览](docs/usage-guide.zh-CN.md#私密浏览)。

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
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

参数 `true` 表示启用该安装。页面加载后，在 DevTools 中执行 `document.documentElement.dataset.monkeySharp`，结果应为 `"ready"`。本例使用 `@grant none`；`none` 或 info 以外的授权需要[显式启用信任选项](#安全模型)，并按需注册对应宿主服务。

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

只需组合已有应用处理器的槽位。浏览器已经初始化，或渲染处理器槽位被未复用的处理器占用时，`Attach` 会拒绝操作。存在 webRequest 服务时，请求处理器槽位也有同样要求，默认服务亦不例外。禁用或替换网络服务的说明见[请求处理器集成](docs/integration-reference.zh-CN.md#web-request)。

## 安全模型

CefSharp 公开 API 会在页面主世界执行引导代码，因此 MonkeySharp 将该桥接报告为 `Unverified`。

- 默认策略执行没有显式 API 授权、声明 `@grant none` 或仅声明 info 授权的脚本。带有 `none` 或 info 以外显式授权的脚本，必须在应用选择 `TrustedPageWorld` 后才会执行。
- 只有应用程序同时信任页面和已安装脚本时，才应通过 `TrustedPageWorld` 启用显式授权的 API。
- 每次执行的能力令牌能防止无关调用者冒充安装实例，但不是浏览器扩展式隔离边界。恶意页面代码可以截获令牌并调用已授予脚本的 API。
- 已安装用户脚本属于可信代码；未知来源需要应用层审核、签名或信任策略。

接受上述限制后再启用特权 API：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

默认模式跳过的执行会产生 `MSR201_BRIDGE_INTEGRITY_REQUIRED`；可信页面模式下，需要桥接的执行会产生 `MSR200_UNVERIFIED_BRIDGE`。仅提供 info 的执行无需桥接绑定或握手。

`@sandbox DOM`（或 `@inject-into content`）选择每安装脚本的独立世界，并通过原生 context 验证的 CDP 派发 GM。raw/默认及 Chromium JavaScript 需求使用页面世界和上述信任政策。DOM 隔离不能暴露真实页面 unsafeWindow，冲突要求和未支持的 unwrap 会拒绝。完整 GM 的 document-start 保持 best effort，OOPIF/BFCache 边界见[执行证据](docs/compatibility/execution-worlds.md)。

## 元数据

元数据涵盖脚本身份、URL 选择、执行时机、授权、依赖、资源及更新/来源地址。解析错误会阻止安装；警告保留在 `MetadataParseResult.Diagnostics` 中。支持字段、匹配规则和示例见[元数据指南](docs/usage-guide.zh-CN.md#元数据)。

`@require` 和 `@resource` 由仓库的 `UserScriptPreparer` 在安装或更新激活前获取并校验。执行时跨 frame 复用已准备内容，支持离线重复执行。Demo 持久化这些版本并提供手动更新。摘要语法、预算和更新规则见[集成参考](docs/integration-reference.zh-CN.md#资源完整性与更新检查)。

静态 `@webRequest` 在安装期编译、准备导航前激活；动态 `GM.webRequest(...)` 替换本执行规则，`[]` 撤销规则。范围与限制见 [P2 实施记录](docs/compatibility/p2-implementation.md)。

## GM API 支持

`GM.info` 和 `GM_info` 无需授权或桥接，在单一契约下始终可用。缺少 `@grant` 或显式声明 `@grant none` 时只提供 info；其他 API 需要所属家族授权及对应宿主提供程序。

下表概述可用能力。参数、返回值、回调及 [Tampermonkey 差异](docs/usage-guide.zh-CN.md#tampermonkey-兼容性检查表)详见[用户脚本使用指南](docs/usage-guide.zh-CN.md#api-索引)。

| API | 可用性 | 说明 |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | 共用不可变的安装信息，无需授权或桥接，默认可用。 |
| `GM.log` / `GM_log` | Core | 传递给 builder 的日志回调。 |
| 值存储与变更监听 | Core | 单键及批量 `getValues`/`setValues`/`deleteValues` API，支持 `GM_*` 别名；按稳定 `ScriptKey` 隔离的类型化值存储（包括已存储的 undefined）、本地同步监听及去重的远端提交通知。 |
| `GM.addStyle`、`GM.addElement` 及别名 | 引导代码 | 页面本地实现。 |
| `GM.getResourceText`、`GM.getResourceUrl`、`GM_getResourceText`、`GM_getResourceURL` | 支持 | 消费安装时已准备的声明资源；旧式名称需要精确 grant。 |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp 默认提供 | 需要 `@connect`；支持重定向、凭据、进度、中止及二进制/multipart 请求体。响应类型和限制见 [HTTP 请求](docs/usage-guide.zh-CN.md#http-请求)。 |
| `GM.registerMenuCommand`、`GM.unregisterMenuCommand` 及别名 | 条件支持 | 需要 `IMenuService`。 |
| `GM.notification`、`GM.setClipboard`、`GM.openInTab` 及别名 | 条件支持 | 需要对应宿主服务。 |
| `GM.download` / `GM_download` | 条件支持 | 需要 `IDownloadService`。现代调用立即返回可取消的完成 Promise；两种入口均支持回调，Demo 共用浏览器网络上下文。详见[下载](docs/usage-guide.zh-CN.md#下载)。 |
| `GM.getTab`、`GM.saveTab`、`GM.getTabs` 及别名 | 条件支持 | 需要 `ITabStateService`。 |
| `GM.cookie` / `GM_cookie` | CefSharp 默认提供 | 通过共享 CDP 验证实际 RequestContext 的 CRUD、SameSite 及精确删除；不提供自有监听或分区支持。 |
| `GM.audio` / `GM_audio` | CefSharp 默认提供 | 原生静音状态、操作及监听；省略 audible，并明确报告部分能力。 |
| `GM.webRequest` / `GM_webRequest` | CefSharp 默认提供 | 有归属的取消/重定向规则、捕获替换、异步结果回调及静态元数据规则；无自有移除句柄。 |
| `window.close`、`window.focus` | 条件支持 | 需要各自的精确授权和 `IUserScriptWindowService`；宿主可以拒绝关闭最后一个标签页。 |
| `window.onurlchange` | Core | 需要精确授权；监听 `pushState`、`replaceState`、`popstate` 和 `hashchange` URL 变化。 |
| `unsafeWindow` | 仅可信页面世界 | 仅在精确授权时公开。 |

除 `GM_info` 外，旧式名称需要精确声明；同步存储/资源别名还需要完整的引导快照。返回值及错误报告方式因 API 而异，部分旧式调用的失败会写入页面控制台。详见[API 契约](docs/integration-reference.zh-CN.md#授权与-api-契约)及[错误处理](docs/usage-guide.zh-CN.md#错误与排查)。

缺少提供程序时，已声明 API 通常不会公开，直接桥接调用返回 `MSP006_NOT_SUPPORTED`；未声明的方法返回 `MSP004_GRANT_DENIED`。

## 宿主服务

Builder 默认使用内存值存储。通过 `UseValueStore`、`UseMenuService` 等方法注册应用服务，提供持久化及其他能力。

默认 XHR、Cookie 和 webRequest 服务使用已附加浏览器的请求上下文。可通过 `UseHttpRequestService`、`UseCookieService`、`UseWebRequestService` 逐项替换，或设置 `EnableDefaultNetworkServices = false` 省略全部三个默认服务。

完整注册示例、所有权规则及提供程序契约见[宿主服务参考](docs/integration-reference.zh-CN.md#宿主服务契约)，其中也说明了 `AddApiProvider` 的所有权转移。

## 生命周期与诊断

每个 `(document, frame, script, run-at)` 组合只执行一次。`@run-at context-menu` 脚本会按匹配的 frame 加入 CefSharp 原生上下文菜单，只有用户选择菜单项后才创建一次新的执行。导航、上下文释放、分离和宿主释放都会使能力失效并取消待处理工作；`@noframes` 阻止子框架执行。

`history.pushState`、`history.replaceState`、`popstate` 和 `hashchange` 产生的同文档 URL 变化不会重新注入脚本。获得 `window.onurlchange` 授权的脚本可通过该属性或 `addEventListener("urlchange", ...)` 订阅。已有 `GM.info` 快照保持不变；后续执行使用当前 URL。

CefSharp 只能提供尽力而为的 document-start 注入。此类执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

应订阅结构化诊断：

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

协议错误码和桥接限制见[集成参考](docs/integration-reference.zh-CN.md#生命周期与诊断)。

## 从 v1 迁移

版本 3 的宿主服务契约不兼容旧源码，并替代了 `Injector`、`JScript`、`IDataStore` 和 WCF 桥接。保留 v1 数据时必须显式迁移存储键。类型对照及服务变更见[迁移说明](docs/integration-reference.zh-CN.md#迁移说明)。

## 构建与测试

在 Windows 上使用支持 `MonkeySharp.slnx` 的 .NET SDK 构建，并安装 Node.js 运行 JavaScript 测试。CI 使用 .NET SDK `9.0.315` 和 Node.js `24`；这些是已测试版本，不代表最低要求。Core 测试目标为 `net8.0`，CefSharp 测试及应用程序目标为 `net462`。

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=121.3.70
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。测试其他受支持版本时，应向所有命令传入相同的 `CefSharpVersion` 属性。打包和真实 Chromium E2E 命令见[集成参考](docs/integration-reference.zh-CN.md#构建打包与验证)。

CI 在 x64/x86 上覆盖 CefSharp `84.4.10`、`121.3.70` 和 `151.3.240`，运行 .NET/JavaScript 测试及真实 Chromium SmokeHost 门禁。WPF Demo 门禁使用默认版本并仅运行 x64。测试覆盖和 CI 日志说明见[兼容性门禁详情](docs/integration-reference.zh-CN.md#chromium-兼容性脚本包)。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
