# MonkeySharp 3.0

[English](README.md) | **中文**

MonkeySharp 3 是面向 CefSharp 嵌入式应用的用户脚本运行时。Core 包含与浏览器无关的解析、URL 匹配、仓库、存储、权限和 API 分发；CefSharp 适配器负责浏览器生命周期集成。

版本 3 的宿主服务契约不兼容旧源码。默认 `LegacyCompatible` 配置通过异步桥接支持常用的 v1 用户脚本接口；旧版 `Injector`、`JScript`、`IDataStore` 和 WCF 桥接仍已移除。

## 软件包

| 软件包 | 目标框架 | 用途 |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`、`netstandard2.0`、`net8.0` | 与浏览器无关的运行时和宿主契约。 |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | 64 位宿主的 CefSharp 生命周期适配器。 |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | 32 位宿主的同一适配器。 |

两个适配器包都公开 `Mzying2001.MonkeySharp.CefSharp` 程序集和命名空间。在 `3.0.0` 包发布前，可以直接引用适配器项目；Core 会被传递引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

应用程序和适配器必须使用同一个显式 `x64` 或 `x86` 进程平台。适配器默认使用 CefSharp `121.3.70`，并已针对 `84.4.10`、`121.3.70` 和 `151.3.240` 进行源码构建验证；发布前仍应针对实际版本编译和测试。版本选择和绑定说明见[集成参考](docs/integration-reference.zh-CN.md#cefsharp-版本兼容性)。

## 快速开始

### WPF 浏览器示例

`Mzying2001.MonkeySharp.Demo` 是可运行的 `net462`/`x64` WPF 浏览器，包含持久化脚本、后台标签页、原生管理器、SQLite 存储和所支持宿主服务的实现。

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
```

可执行文件目录必须可写，因为便携 profile 存储在其中的 `Data/` 目录。Demo 显式启用了 `TrustedPageWorld`：它是面向可信页面和脚本的集成示例，不是加固浏览器或完整的 Tampermonkey 替代品。使用方式、恢复流程、服务边界和验证方法见 [Demo 指南](Mzying2001.MonkeySharp.Demo/README.md)。

### 最小宿主集成

安装脚本、构建宿主、在浏览器初始化前附加宿主，然后把浏览器加入界面：

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
var browser = new CefSharp.WinForms.ChromiumWebBrowser("about:blank");
host.Attach(browser);
form.Controls.Add(browser);
```

参数 `true` 表示启用该安装。本例使用 `@grant none`；需要宿主支持的授权还必须启用下述信任选项并注册提供程序。默认 XHR、Cookie 和 webRequest 服务跟随已附加浏览器的请求上下文，也可以通过 builder 禁用或替换。

创建第一个浏览器前，应为整个进程启用并发 Task 绑定。不要启用 CefSharp WCF：

```csharp
CefSharpSettings.ConcurrentTaskExecution = true;
```

应先释放 MonkeySharp 宿主，再释放浏览器。重复释放是安全的，并会取消文档会话和待处理 API 请求。

### 已有的渲染处理器

CefSharp 只提供一个 `IRenderProcessMessageHandler` 槽位。附加 MonkeySharp 前应复用已有处理器：

```csharp
browser.RenderProcessMessageHandler =
    new RenderProcessMessageHandlerMultiplexer(applicationHandler);
host.Attach(browser);
```

如果浏览器已经初始化，或槽位被未复用的处理器占用，`Attach` 会拒绝操作。

## 安全模型

CefSharp 公开 API 会在页面主世界执行引导代码，因此 MonkeySharp 将该桥接报告为 `Unverified`。

- 默认策略执行 `@grant none` 脚本，并跳过请求宿主 API 的脚本。
- 只有应用程序同时信任页面和已安装脚本时，才应通过 `TrustedPageWorld` 启用宿主 API。
- 每次执行的能力令牌能防止无关调用者冒充安装实例，但不是浏览器扩展式隔离边界。恶意页面代码可以截获令牌并调用已授予脚本的 API。
- 已安装用户脚本属于可信代码；未知来源需要应用层审核、签名或信任策略。

接受上述限制后再启用特权 API：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

受影响的执行会产生 `MSR200_UNVERIFIED_BRIDGE`；适配器不能声明已验证的完整性。

## 元数据

默认解析器允许 `// ==UserScript==` 前存在有界前导内容；`ModernStrict` 要求第一行非空内容就是头部。错误会阻止安装，警告则保留在 `MetadataParseResult.Diagnostics` 中。

支持的字段包括：

- 身份：`@name`、本地化名称、`@namespace`、`@version`、`@description`、`@author`、`@license` 和图标。
- 选择：`@match`、`@include`、`@exclude`、`@exclude-match` 和 `@noframes`。
- 执行：`@run-at`，以及保留的 `@run-in`、`@inject-into` 值。
- 能力与资源：`@grant`、`@connect`、`@require` 和 `@resource`。
- 源地址：`@downloadURL`、`@updateURL`、`@homepageURL` 和 `@supportURL`。

排除规则优先于正向规则。URL 片段会被忽略，主机名经过 IDN 规范化，显式端口会被检查，且 `*.example.com` 不匹配裸域名。缺少 `@grant` 等同于 `@grant none`；已知旧式别名会被规范化，未知授权保持大小写敏感且不会公开。

## GM API 支持

只有脚本声明了精确授权且宿主存在所需提供程序时，才会公开规范 `GM.*` API 和旧式别名。

| API | 可用性 | 说明 |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | 不可变的安装信息。 |
| `GM.log` / `GM_log` | Core | 传递给 builder 的日志回调。 |
| `GM.getValue`、`GM.setValue`、`GM.deleteValue`、`GM.listValues` 及别名 | Core | 按稳定 `ScriptKey` 隔离的 JSON 存储。 |
| `GM.addValueChangeListener`、`GM.removeValueChangeListener` 及别名 | Core | 通知同一安装实例的活动执行。 |
| `GM.addStyle`、`GM.addElement` 及别名 | 引导代码 | 页面本地实现。 |
| `GM.getResourceText`、`GM.getResourceURL` 及别名 | 条件支持 | 需要 `IResourceProvider` 和已声明的 `@resource`。 |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | CefSharp 默认提供 | 支持重定向、凭据、进度、中止、二进制/多段请求体及多种响应类型；需要 `@connect`。 |
| `GM.registerMenuCommand`、`GM.unregisterMenuCommand` 及别名 | 条件支持 | 需要 `IMenuService`。 |
| `GM.notification`、`GM.setClipboard`、`GM.openInTab`、`GM.download` 及别名 | 条件支持 | 需要对应宿主服务。 |
| `GM.getTab`、`GM.saveTab`、`GM.getTabs` 及别名 | 条件支持 | 需要 `ITabStateService`。 |
| `GM.cookie` / `GM_cookie` | CefSharp 默认提供 | 结构化访问浏览器请求上下文。 |
| `GM.webRequest` / `GM_webRequest` | CefSharp 默认提供 | Tampermonkey selector/action 注册、可移除 handle，以及 cancel/redirect 结果回调。 |
| `unsafeWindow` | 仅可信页面世界 | 仅在精确授权时公开。 |

现代存储和资源读取返回 Promise；旧式别名使用有界的同步引导快照。旧式回调 API 会立即返回句柄或 ID，并将回调故障报告为诊断。

缺少提供程序时，已声明 API 通常不会公开，直接桥接调用返回 `MSP006_NOT_SUPPORTED`；未声明的方法返回 `MSP004_GRANT_DENIED`。XHR、Cookie 和 webRequest 的详细行为见[网络 API 参考](docs/integration-reference.zh-CN.md#网络-api)。

## 宿主服务

通过 builder 注册应用程序拥有的能力。默认网络服务可以保留或替换：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseDependencyProvider(cachedDependencyProvider)
    .UseResourceProvider(resourceProvider)
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .UseCookieService(cookieService)          // 可选：替换默认服务
    .UseWebRequestService(webRequestService) // 可选：替换默认服务
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

应用程序拥有仓库、浏览器和传入的服务；宿主拥有其引擎、网关和内部创建的提供程序。详细的所有权、一致性、依赖、重定向和流式处理要求见[宿主服务参考](docs/integration-reference.zh-CN.md#宿主服务契约)。

## 生命周期与诊断

每个 `(document, frame, script, run-at)` 组合只执行一次。导航、上下文释放、分离和宿主释放都会使能力失效并取消待处理工作；`@noframes` 阻止子框架执行。

CefSharp 只能提供尽力而为的 document-start 注入。此类执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

应订阅结构化诊断：

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

协议错误码和桥接限制见[集成参考](docs/integration-reference.zh-CN.md#生命周期与诊断)。

## 从 v1 迁移

| v1 | 3.0 |
| --- | --- |
| `JScript` / 随机 `ScriptId` | 仓库管理的 `UserScriptInstallation` / 稳定 `ScriptKey` |
| `JScriptMeta` 字典 | 不可变 `UserScriptMetadata` 和解析器诊断 |
| `InjectorBase` 上的 `IList<JScript>` | `IUserScriptRepository` 快照和变更事件 |
| `IDataStore` / `MemDataStore` | `IUserScriptValueStore` / `InMemoryUserScriptValueStore` |
| `Injector.AttachBrowser` | 构建宿主，并在浏览器初始化前调用 `Attach` |
| 同步 Messenger/WCF | 通过异步字符串绑定传输的版本化 JSON |
| `GM_*` 全局对象 | 默认 `LegacyCompatible` 配置提供的当前桥接别名 |
| `IScriptVerifier` 字符扫描器 | 元数据诊断及 Chromium 语法/运行时报告 |

保留 v1 数据时必须显式迁移存储键。自定义宿主服务也需要修改源码，详见[迁移说明](docs/integration-reference.zh-CN.md#迁移说明)。

## 构建与测试

仓库使用 .NET SDK `9.0.315`，默认 CefSharp 版本为 `121.3.70`：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。测试其他受支持版本时，应向所有命令传入相同的 `CefSharpVersion` 属性。打包和真实 Chromium E2E 命令见[集成参考](docs/integration-reference.zh-CN.md#构建打包与验证)。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
