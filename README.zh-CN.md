# MonkeySharp 3.0

[English](README.md) | **中文**

MonkeySharp 3 是面向嵌入 CefSharp 的应用程序的用户脚本运行时。它将与浏览器无关的脚本解析、匹配、存储、权限和 API 分发逻辑，与 CefSharp 生命周期适配器分离。

第 3 版对宿主服务契约进行了不兼容变更。默认的 `LegacyCompatible` 配置通过第 1 版异步桥接协议提供常见的 v1 用户脚本接口。v1 中的 `Injector`、`JScript`、`IDataStore` 和 WCF 桥接仍已移除，不会恢复同步 WCF 传输。

## 软件包

| 软件包 | 目标框架 | 用途 |
| --- | --- | --- |
| `Mzying2001.MonkeySharp.Core` | `net462`、`netstandard2.0`、`net8.0` | 元数据、匹配、仓库、引擎、存储、权限、桥接和宿主服务契约。不依赖 CefSharp。 |
| `Mzying2001.MonkeySharp.CefSharp.x64` | `net462` x64 | CefSharp 框架（frame）生命周期、异步绑定、脚本执行和诊断。适配器针对所选的 `CefSharpVersion` 编译（默认为 `121.3.70`）。 |
| `Mzying2001.MonkeySharp.CefSharp.x86` | `net462` x86 | 同一适配器，显式编译为面向 32 位进程的版本。 |

两个适配器软件包均提供 `Mzying2001.MonkeySharp.CefSharp` 程序集和命名空间。使用不同的软件包 ID，可避免相同版本的 x64 和 x86 构建产物相互覆盖。

软件包版本为 `3.0.0`。在正式发布前，可从本仓库同级目录中的应用程序添加项目引用；Core 会作为传递依赖被引用：

```xml
<ItemGroup>
  <ProjectReference Include="..\MonkeySharp\Mzying2001.MonkeySharp.CefSharp\Mzying2001.MonkeySharp.CefSharp.csproj" />
</ItemGroup>
```

构建使用方应用程序及其项目引用时，应使用与进程一致的 `Platform=x64` 或 `Platform=x86`。CefSharp 不支持将此适配器作为 `AnyCPU` 使用。

### CefSharp 版本兼容性

适配器无法通过同一个二进制文件支持任意 CefSharp 版本。CefSharp 程序集具有强名称，且不同版本的公共接口存在变化（例如，`IBrowser.GetFrame(long)` 变成了 `GetFrameByIdentifier(string)`，`IFrame.Identifier` 从 `long` 变成了 `string`）。因此，不能假定针对某一版本编译的软件包能够在所有其他版本上运行。

适配器项目保留固定的默认版本（`121.3.70`），同时允许宿主选择实际使用的确切 CefSharp 版本。在还原和构建适配器之前设置 `CefSharpVersion`（该属性也会通过 `ProjectReference` 传递）：

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

适配器源码为新旧框架查找和标识符 API 提供了兼容层，并已针对 `84.4.10`、`121.3.70` 和 `151.3.240` 验证。这是经过测试的兼容版本集合，并不承诺未来每个 CefSharp 版本都具有二进制兼容性；请针对实际发布的每个版本进行编译和测试。

适配器的 `CefSharp.Common` 引用不会作为其 NuGet 包的传递依赖公开，因此不会向应用程序强制引入冲突的 CefSharp 版本。软件包使用方必须自行引用匹配的 `CefSharp.WinForms`、`CefSharp.Wpf` 或 `CefSharp.Common` 包；对于 .NET Framework 应用程序，还必须允许正常的 CefSharp 绑定重定向。所有 CefSharp 包、原生 CEF 二进制文件以及 MonkeySharp 适配器所针对的 CefSharp 版本必须保持一致。

## 快速开始

### WPF 浏览器示例

`Mzying2001.MonkeySharp.Demo` 是可运行的 `net462`/`x64` WPF 浏览器，使用 `CefSharp.Wpf`、`CommunityToolkit.Mvvm` 和 SQLite。它提供持久化用户脚本、真正的后台标签页、原生脚本编辑器和管理器、安装权限审核、脚本菜单、通知、剪贴板写入、标签页句柄、下载、资源，以及适配器默认的 XHR/Cookie/webRequest 服务。每个保留的浏览器控件都有独立的 MonkeySharp 宿主；标签页之间共享脚本仓库、GM 值存储和 Chromium 请求上下文。

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\Mzying2001.MonkeySharp.Demo.exe
```

可执行文件所在目录必须可写。数据保存在该目录下的 `Data/` 中，而不是 shell 的工作目录中，包括：`BrowserCache/`、`UserScripts/`、`Dependencies/`、`Downloads/`、`Logs/` 和 `monkeysharp.db`。配置目录锁可防止两个实例同时写入同一份数据。解决方案的 x86 配置仍会构建库和测试，但不包含仅支持 x64 的 Demo 项目。

**Demo 显式启用了 `TrustedPageWorld`。它是受信任环境下的集成示例，并非安全的通用浏览器，也不是 Tampermonkey 的完整替代品。** 浏览器和脚本管理器中也会显示此警告。有关使用方法、恢复方式、已实现的宿主服务边界和手动验证场景，请阅读 `Mzying2001.MonkeySharp.Demo/README.md`。

```powershell
dotnet test Mzying2001.MonkeySharp.Demo.Tests/Mzying2001.MonkeySharp.Demo.Tests.csproj -c Release -p:Platform=x64
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
```

WPF E2E 验证使用独立的配置目录和本地回环 HTTP 服务器，覆盖真正的后台标签页、共享存储和 Cookie、资源、XHR、webRequest、菜单、通知点击、下载、框架、导航、脚本启停和资源释放。它不能替代现有的 WinForms SmokeHost 验证。

### 最小宿主集成

将脚本安装到仓库中，构建宿主，在浏览器初始化之前附加宿主，然后创建浏览器。

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

// Attach before the control handle is created.
var browser = new CefSharp.WinForms.ChromiumWebBrowser("about:blank");
host.Attach(browser);
// Add the control to the form after Attach. CefSharp 121 creates the native
// browser when the WinForms control handle is created.
form.Controls.Add(browser);
```

参数 `true` 表示启用该安装实例。此最小示例特意使用 `@grant none`；申请宿主支持权限的脚本，还需要完成下文所述的服务注册并显式启用信任模式。

`CefSharpUserScriptHostBuilder` 默认启用 `GM.xmlHttpRequest`、`GM.webRequest` 和 `GM.cookie` 服务。设置 `EnableDefaultNetworkServices = false` 可保留显式注入服务的行为，也可调用相应的 `Use...Service` 方法替换某项服务。默认服务使用当前附加浏览器的请求上下文，并随宿主一起释放；应用程序提供的服务仍由应用程序管理。

浏览器关闭时应释放宿主。`Dispose` 是幂等的，会取消文档会话和待处理的 API 请求。

```csharp
host.Dispose();
browser.Dispose();
```

不要启用 `CefSharpSettings.WcfEnabled`。MonkeySharp 仅使用 CefSharp 异步 JavaScript 绑定。
创建第一个浏览器之前，请为进程启用一次 CefSharp 的并发 Task 绑定：

```csharp
CefSharpSettings.ConcurrentTaskExecution = true;
```

### 兼容性配置

`CefSharpHostOptions.Compatibility` 默认为 `LegacyCompatible`。它接受 `GM_getValue` 等旧版 `@grant` 别名，以非严格模式的包装语义公开相应的 `GM_*` 全局对象，并在引导阶段建立有大小限制的同步存储和资源镜像。旧版回调接口（`GM_xmlhttpRequest`、菜单注册和标签页回调）与对应的 `GM.*` 接口共享相同的经过身份验证的桥接操作。

如果脚本必须保持严格模式的包装语义，并仅使用 Promise 风格的 API 名称，请使用 `ModernStrict`：

```csharp
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Parsing;

var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions
    {
        Compatibility = new UserScriptCompatibilityOptions
        {
            Profile = UserScriptCompatibilityProfile.ModernStrict
        }
    })
    .Build();
```

宿主选择执行时使用的兼容性配置。元数据解析配置则在创建仓库时选择，因此已安装的元数据不会在运行时重新解释：

```csharp
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Parsing;

var parser = new UserScriptMetadataParser(
    new UserScriptMetadataParserOptions
    {
        Profile = UserScriptCompatibilityProfile.ModernStrict,
        AllowHeaderPreamble = false
    });
var repository = new InMemoryUserScriptRepository(parser);
```

### 已有的渲染处理器

CefSharp 只提供一个 `IRenderProcessMessageHandler` 槽位。附加 MonkeySharp 之前，请显式组合处理器：

```csharp
browser.RenderProcessMessageHandler =
    new RenderProcessMessageHandlerMultiplexer(applicationHandler);
host.Attach(browser);
```

如果浏览器已经初始化，或处理器槽位已被非多路复用处理器占用，`Attach` 会拒绝附加。它不会静默替换应用程序的处理器。

## 安全模型

CefSharp 121.3.70 的公共 API 在页面的主世界（main world）中执行引导代码。因此，MonkeySharp 将 CefSharp 桥接的完整性标记为 `Unverified`。

- 默认策略会运行 `@grant none` 脚本，但不会运行请求宿主支持的 GM API 的脚本。
- `TrustedPageWorld` 是一种显式启用的兼容模式，适用于应用程序信任的页面和已安装脚本。
- 每次执行使用一个 256 位能力令牌，防止不知道该令牌的调用方冒充其他安装实例。但在页面主世界中，这并不是 Chromium 扩展式的隔离边界。
- 恶意页面可以在引导代码运行前包装 JavaScript 内置函数和 CefSharp 绑定函数，截获能力令牌，进而调用授予该脚本的所有 API。不能将令牌视为防御页面本身的保护措施。
- 已安装的用户脚本可以修改页面，属于受信任代码。项目不会将字符扫描用作沙箱，也不会将其描述为沙箱。
- 对于来源未知的脚本，应用程序需要提供签名验证、审核或信任策略。

只有接受上述限制后，才应启用宿主 API：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions
    {
        TrustedPageWorld = true
    })
    .Build();
```

受影响文档中的每次特权脚本执行都会产生 `MSR200_UNVERIFIED_BRIDGE` 诊断。无法通过配置让适配器宣称具有 `Verified` 完整性。

## 元数据

默认的旧版兼容解析器允许在 `// ==UserScript==` 之前出现 BOM、空行、许可证注释和有限长度的工具注释。扫描范围限制为 128 行和 64 KiB；元数据错误仍会阻止安装，警告会保留在 `MetadataParseResult.Diagnostics` 中。`ModernStrict` 要求第一个非空行就是元数据头部，并禁用前导内容扫描。

支持的字段：

- 标识和显示：`@name`、本地化的 `@name:locale`、`@namespace`、`@version`、`@description`、`@author`、`@license`、`@icon`/`@iconURL`。
- 匹配规则：`@match`、`@include`、`@exclude`、`@exclude-match`、`@noframes`。
- 执行时机：`@run-at document-start|document-body|document-end|document-idle`，同时保留 `@run-in` 和 `@inject-into` 的值。
- 能力声明：`@grant`、`@connect`、`@require` 和 `@resource`。
- 来源元数据：`@downloadURL`、`@updateURL`、`@homepageURL` 和 `@supportURL`。

排除规则优先于包含规则。至少必须匹配一条 `@match` 或 `@include` 规则。匹配时忽略 URL 片段，对主机名进行 IDN 规范化，并检查显式端口；`*.example.com` 匹配子域名，但不匹配根域名本身。

未声明 `@grant` 等同于 `@grant none`。已知的旧版别名会规范化为标准能力名称，而原始拼写保留在 `UserScriptMetadata.DeclaredGrants` 和 `GM.info` 中。未知权限名称仍区分大小写，会产生警告，且不会公开相应能力。

## GM API 支持

在具备相应权限和宿主提供程序时，标准 `GM.*` 方法和旧版兼容接口均可使用。`GM.getValue`、`GM.listValues`、`GM.getResourceText` 和 `GM.getResourceURL` 仍然返回 Promise；对应的 `GM_*` 别名则同步读取当前执行实例在引导阶段建立的有限大小镜像。回调风格的旧版 API 会立即返回旧版句柄或 ID，并将回调失败报告为诊断信息。

| API | 可用条件 | 说明 |
| --- | --- | --- |
| `GM.info` / `GM_info` | Core | 冻结的安装信息；需要精确对应的权限。`GM_info` 保留权限声明的原始拼写。 |
| `GM.log` / `GM_log` | Core | 传递给构建器的 `LogTo` 回调。 |
| `GM.getValue`, `GM.setValue`, `GM.deleteValue`, `GM.listValues` / `GM_getValue`, `GM_setValue`, `GM_deleteValue`, `GM_listValues` | Core | 通过稳定的 `ScriptKey` 隔离的规范化 JSON 存储；现代方法为异步操作，旧版镜像读取为同步操作，写入按顺序排队。 |
| `GM.addValueChangeListener`, `GM.removeValueChangeListener` / `GM_addValueChangeListener`, `GM_removeValueChangeListener` | Core | 仅向同一安装实例的活动执行实例发送通知。 |
| `GM.addStyle`, `GM.addElement` / `GM_addStyle`, `GM_addElement` | 引导代码 | 在页面内实现；仍需精确对应的权限。 |
| `GM.getResourceText`, `GM.getResourceURL` / `GM_getResourceText`, `GM_getResourceURL` | 有条件支持 | 需要 `IResourceProvider` 和已声明的 `@resource`；旧版读取使用引导阶段生成的有限大小快照。 |
| `GM.xmlHttpRequest` / `GM_xmlhttpRequest` | 有条件支持 | CefSharp 宿主默认创建 `CefSharpHttpRequestService`；应用程序可通过 `UseHttpRequestService` 替换。支持二进制和 multipart 请求体、重定向、凭据、进度、中止，以及文本/JSON/二进制/blob/流响应。初始 URL 和重定向 URL 都必须满足 `@connect`。 |
| `GM.registerMenuCommand`, `GM.unregisterMenuCommand` / `GM_registerMenuCommand`, `GM_unregisterMenuCommand` | 有条件支持 | 需要 `IMenuService`；旧版接口同步分配 ID，异步完成注册。 |
| `GM.notification` / `GM_notification` | 有条件支持 | 需要 `INotificationService`；旧版回调接收点击和完成生命周期通知。 |
| `GM.setClipboard` / `GM_setClipboard` | 有条件支持 | 需要 `IClipboardService`。 |
| `GM.openInTab` / `GM_openInTab` | 有条件支持 | 需要 `ITabService`；旧版 `close()` 调用宿主，并公开 `closed` 状态。 |
| `GM.download` / `GM_download` | 有条件支持 | 需要 `IDownloadService`；旧版回调接收进度、成功、失败和中止事件。 |
| `GM.getTab`, `GM.saveTab`, `GM.getTabs` / `GM_getTab`, `GM_saveTab`, `GM_getTabs` | 有条件支持 | 需要 `ITabStateService`；旧版标签页方法使用回调。 |
| `unsafeWindow` | 仅限受信任页面主世界 | 仅在精确声明 `@grant unsafeWindow` 时绑定。 |
| `GM.cookie` / `GM_cookie` | 有条件支持 | CefSharp 宿主创建基于请求上下文的 `CefSharpCookieService`；应用程序可通过 `UseCookieService` 替换。列出、设置、删除操作，以及由服务发起的变更监听，均使用浏览器请求上下文。 |
| `GM.webRequest` / `GM_webRequest` | 有条件支持 | CefSharp 宿主创建 `InMemoryWebRequestService` 并附加 `CefSharpWebRequestHandler`；应用程序可通过 `UseWebRequestService` 替换。规则覆盖请求、响应和身份验证阶段，回调用于观察事件。 |

如果脚本直接请求已声明但没有提供程序的 API，会返回协议错误 `MSP006_NOT_SUPPORTED`。正常使用时，能力发现机制会阻止引导代码公开该方法。

在默认的未验证模式下，具有宿主支持权限的脚本会被整体跳过，因此无法检查 `GM`。在 `TrustedPageWorld` 中，即使声明了精确对应的权限，如果没有注册相应能力，该方法仍为 undefined；直接发起桥接请求仍会收到 `MSP006_NOT_SUPPORTED`。未声明的方法会收到 `MSP004_GRANT_DENIED`。

### HTTP 请求 API

声明 `@grant GM.xmlHttpRequest` 或 `@grant GM_xmlhttpRequest`，并为每个目标声明 `@connect` 条目。`details.url` 接受字符串或 `URL`。HTTP 方法不区分大小写，只要是有效的 HTTP token 就不受其他限制，但 `CONNECT`、`TRACE` 和 `TRACK` 除外。

请求体支持字符串、`ArrayBuffer`、类型化数组和 `DataView`、`Blob`/`File`、`FormData`、`URLSearchParams`、普通对象和数组。普通对象和数组使用 JSON；`URLSearchParams` 使用表单编码；`FormData` 保留其生成的 multipart 边界、文件名和媒体类型。请求数据以 64 KiB 分块跨桥接传输，而不是作为一条完整的 Base64 消息发送。

支持的控制选项包括 `headers`、`cookie`、`user`、`password`、`anonymous`、`overrideMimeType`、`signal`、`redirect`、`nocache`、`revalidate`、`fetch`、`timeout` 和 `context`。重定向模式默认为 `follow`，也接受 `error` 和 `manual`。follow 模式遵循 Chromium 对 301/302/303 的方法转换规则，并在 307/308 时保留方法和请求体。每个重定向目标都会重新检查权限、`@connect` 和宿主权限策略；跨源重定向会移除显式设置的 Cookie 和授权信息。

允许设置用于兼容的请求头，包括 `User-Agent`、`Referer`、`Origin` 和 `Cookie`。MonkeySharp 拒绝 `Host`、`Content-Length`、连接和传输控制头，以及所有 `Sec-*` 和 `Proxy-*` 头。`anonymous:true` 禁用 CefSharp 请求上下文中存储的 Cookie 和凭据。`fetch:true` 忽略超时和进度，仅报告 readyState 4，与 Tampermonkey 文档中的 fetch 模式限制一致。

`responseType` 接受 `text`（默认值）、`json`、`arraybuffer`、`blob` 和 `stream`。流响应公开可取消的 `ReadableStream`；其他类型由从宿主读取的 64 KiB 分块组装而成。响应对象包含取值为 1 至 4 的 `readyState`、`status`、`statusText`、`finalUrl`、`responseHeaders`、`response`、适用时的 `responseText`，以及原始 JavaScript `context` 引用。`responseHeaders` 是以 CRLF 分隔的原始响应头字符串，并保留重复字段；此前非标准的 `responseBase64` 字段已移除。HTTP 4xx 和 5xx 响应调用 `onload`；网络、权限、请求体序列化和桥接失败调用 `onerror`。

生命周期回调包括 `onloadstart`、`onreadystatechange`、`onprogress`、`onuploadprogress`（或 `upload.onprogress`）、`onload`、`onerror`、`ontimeout`、`onabort` 和 `onloadend`。回调以同一个稳定的响应对象作为 `this`；单个回调抛出异常时会报告到控制台，但不会改变最终结果或阻止清理。`GM.xmlHttpRequest` 返回带有 `.abort()` 的 Promise，而 `GM_xmlhttpRequest` 立即返回中止句柄，并通过回调传递结果。`AbortSignal` 和流取消操作都会传递到宿主请求。

请求和响应大小默认不设上限。两个方向的数据各自在 256 KiB 以内保存在内存中，超过后转存到临时文件；临时文件在完成、中止、失败、导航或宿主释放时删除。HTTP 请求体和响应体传输以及长时间运行的 `execute` 操作，不受桥接通用的 1 MiB 消息大小和 30 秒请求时限约束；`details.timeout`、显式中止和页面销毁仍会取消请求。

使用 `proxy`、`cookiePartition`，或将 `Blob`/`File` 用作 URL，都会以 `MSP006_NOT_SUPPORTED` 失败。分区 Cookie 键属于浏览器扩展管理器状态，CefSharp 的公共请求上下文 API 并未公开。CefSharp `84.4.10` 使用旧版请求和 post-data 工厂，121/151 使用当前工厂；这三个版本的构建均遵循同一套已记录的 HTTP 行为。请对应用程序实际发布的每个确切 CefSharp 版本运行真实浏览器测试。

### Cookie API

声明 `@grant GM.cookie`（或旧版别名 `GM_cookie`）。CefSharp 宿主自动使用当前附加浏览器的请求上下文；显式调用 `UseCookieService` 仍具有更高优先级。URL 必须是绝对 HTTP 或 HTTPS URL；Core 会校验域名、路径、secure、SameSite 和过期字段。`GM.xmlHttpRequest` 提供独立的、面向兼容性的显式 Cookie 请求头支持；`GM.cookie` 则是结构化的浏览器 Cookie 存储 API，`httpOnly` 的可见性由该服务决定，而非 `document.cookie`。

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();

// For an application-owned implementation, call UseCookieService(...) here.
```

现代接口返回 Promise：

```javascript
const cookies = await GM.cookie.list({ url: location.href });
await GM.cookie.set({ url: location.href, name: "session", value: "ready", path: "/" });
await GM.cookie.delete({ url: location.href, name: "session" });
const listenerId = GM.cookie.addListener({ url: location.href }, change => console.log(change.cause));
```

`GM_cookie.list/set/delete` 使用回调，`addListener/removeListener` 使用数字 ID。执行实例、文档、框架或宿主释放时，会释放对应的监听器注册。

### Web Request API

声明 `@grant GM.webRequest`（或 `GM_webRequest`）。CefSharp 自动使用 `InMemoryWebRequestService` 并挂载 `CefSharpWebRequestHandler`；调用 `UseWebRequestService(...)` 可提供由应用程序管理的实现。规则在注册时校验，并在每次请求时重新检查权限策略。阻止、重定向、请求头修改和身份验证决策均通过预注册的宿主规则完成；用户脚本回调不会在网络线程上运行，也无法延迟请求。

```javascript
const ruleId = await GM.webRequest.addRule({
  id: "strip-tracking",
  phase: "OnBeforeRequest",
  priority: 100,
  filter: { urlPatterns: ["https://example.com/*"], resourceTypes: ["Script"] },
  action: { kind: "ModifyRequestHeaders", headers: { "X-Userscript": "1" } }
});
const listenerId = GM.webRequest.addListener({}, event => console.log(event.phase, event.url));
```

`GM_webRequest(rules, listener)` 是旧版注册形式，返回可移除的句柄。规则按优先级降序、注册顺序升序执行；阻止和重定向会终止后续评估，后执行的请求头规则会覆盖先前的值。如果请求处理器槽位已被非多路复用处理器占用，CefSharp 适配器会拒绝附加，而不是静默替换它。

## 宿主服务

注册由应用程序管理的能力（除非禁用，否则默认提供网络服务）：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseDependencyProvider(cachedDependencyProvider)
    .UseResourceProvider(resourceProvider)
    .UseHttpRequestService(httpService) // optional override of the CefSharp default
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .UseCookieService(cookieService) // optional override of the CefSharp default
    .UseWebRequestService(webRequestService) // optional override of the in-memory default
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();

// Set EnableDefaultNetworkServices = false to require explicit network services.
```

构建器只能使用一次。对象所有权规则如下：

| 对象 | 所有者和释放规则 |
| --- | --- |
| `CefSharpUserScriptHost` | 由应用程序管理，应在浏览器之前释放。它拥有其引擎、网关、内部创建的 API 提供程序、默认网络服务和默认内存存储。 |
| `IUserScriptRepository` 和 CefSharp 浏览器 | 由应用程序管理；宿主不会释放它们。 |
| 传递给 `UseValueStore`、`UsePermissionPolicy` 或任意 `Use...Service` / `Use...Provider` 方法的对象 | 由应用程序管理；宿主不会释放这些服务对象，包括 `IResourceProvider` 和 `IUserScriptDependencyProvider`。 |
| 传递给 `AddApiProvider` 的对象 | 所有权转移给网关；如果对象实现了 `IDisposable`，网关会调用其 `Dispose`。 |
| `IMenuService` 返回的菜单注册对象 | 由宿主内部提供程序管理；在注销、执行结束或宿主释放时释放。 |

服务的重要要求：

- `IUserScriptValueStore` 对同一 `ScriptKey` 的操作必须具有可线性化语义。提交前取消不会产生任何变更；提交后取消仍应成功完成，并发出一次通知。
- `IUserScriptDependencyProvider` 负责 `@require` 的可信下载、缓存、HTTPS 策略以及可选的哈希或签名验证。`ResourceScriptSourceResolver` 保留声明顺序，并执行配置的字节数限制。
- 配置了 `IUserScriptDependencyProvider` 时，会在规划主脚本源码之前解析 `@require`，该过程不使用 GM 权限。在 `LegacyCompatible` 中，声明 `@require` 却未配置依赖提供程序，会跳过该次执行并产生 `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE`；已配置的提供程序失败时，产生 `MSR400_DEPENDENCY_RESOLUTION_FAILED`。在 `ModernStrict` 中，需要配置提供程序才能加载声明的依赖；如果没有提供程序，则直接使用已安装的主脚本源码。
- `IResourceProvider` 必须返回对应名称声明下已获授权的内容。资源大小受 `BridgeOptions.MaxResourceBytes` 限制；HTTP 请求体和响应体不受该资源限制约束。
- `IHttpRequestService` 在执行开始前接收可重复读取的 `IUserScriptHttpBody` 和观察者。当 `UserScriptHttpRequest.MaxResponseBytes` 不为 null 时，必须遵守该限制；必须先报告响应元数据，再报告响应体数据，并在跟随每次重定向前调用 `RedirectAllowed`。它还必须在 `UserScriptHttpResponse.RedirectUrls` 中报告已跟随的重定向；Core 会根据 `@connect` 重新检查该重定向链和最终 URL。
- 对每个由宿主支持的请求，`IUserScriptPermissionPolicy` 都会接收安装实例、框架、方法、目标摘要以及当前宿主能力。

## 生命周期与诊断

CefSharp 适配器为每个框架文档创建标识，并对每个 `(document, frame, script, run-at)` 组合只执行一次。主框架和子框架都会被监听；`@noframes` 阻止子框架执行。导航、上下文释放、分离和资源释放都会使能力令牌失效，并取消待处理的工作。

CefSharp 的公共钩子仅提供 `BestEffortDocumentStart`：注入在渲染器上下文回调之后开始，可能晚于页面内联脚本。每次选中的 document-start 执行都会在运行脚本前产生 `MSR100_DOCUMENT_START_BEST_EFFORT`。设置 `RequireGuaranteedDocumentStart = true` 后，会跳过此类脚本并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

请订阅结构化诊断，而不是依赖被吞掉的异常：

```csharp
host.Diagnostic += (_, diagnostic) =>
    applicationLog.Write($"{diagnostic.Code}: {diagnostic.Message}");
```

协议错误使用稳定的错误码 `MSP001` 至 `MSP010`，以及 `MSP999`。诊断信息绝不会包含能力令牌或存储的值。

桥接默认限制为：每个请求 1 MiB、每个响应 1 MiB、每个资源 10 MiB、普通请求超时 30 秒，以及每个文档最多 64 个待处理请求。可通过 `CefSharpHostOptions.Bridge` 配置。XHR 请求体和响应体的分块保持在消息限制以内；XHR `execute` 不受普通请求超时限制，因为它具有独立的生命周期和 `details.timeout`。

## 迁移到 3.0

| v1 | 3.0 |
| --- | --- |
| `JScript` / 随机 `ScriptId` | 由仓库管理的 `UserScriptInstallation` / 稳定的 `ScriptKey` |
| `JScriptMeta` 字典 | 不可变的 `UserScriptMetadata` 和解析器诊断 |
| `InjectorBase` 上的 `IList<JScript>` | `IUserScriptRepository` 快照和变更事件 |
| `IDataStore` / `MemDataStore` | `IUserScriptValueStore` / `InMemoryUserScriptValueStore` |
| `Injector.AttachBrowser` | 构建 `CefSharpUserScriptHost`，然后在浏览器初始化之前调用 `Attach` |
| 同步 Messenger/WCF | 通过异步字符串绑定传输的版本化 JSON 协议 |
| `GM_getValue` 及其他 `GM_*` | 第 1 版桥接协议上的 `LegacyCompatible` 别名；存储和资源读取使用同步执行快照，写入按顺序异步执行 |
| 一次性传输的 `GM_xmlhttpRequest` 请求数据和结果 | 使用 `create`、`appendBody`、`execute`、`readBody`、`abort` 和 `release` 的生命周期会话；自定义 HTTP 服务需更新为观察者和操作契约 |
| `ForceUseStrict` 和 `with(window)` | 带有显式 `GM`、`unsafeWindow` 和 `window` 参数的严格模式 `Function` 作用域 |
| `IScriptVerifier` 字符扫描器 | 严格的元数据诊断；JavaScript 语法和运行时错误由 Chromium 报告 |

兼容性已内置于默认配置中；没有单独的传输层或 WCF 软件包。如果需要保留现有 v1 数据，请显式迁移存储键。要求严格语言语义的应用程序可以选择 `ModernStrict`，并将元数据权限更新为标准 `GM.*` 名称。

3.0 的宿主契约变更会破坏现有服务实现的源码兼容性。`IHttpRequestService.SendAsync` 在开始前接收 `IUserScriptHttpObserver`，并返回 `IHttpRequestOperation`；请求体是可重复读取的流，响应数据以增量方式传递，重定向授权为异步操作。现有适配器应将原有的 task/future 包装到操作对象中，在工作开始前附加观察者，并转发响应开始、数据、进度、完成和中止事件。通知、标签页和下载服务同样返回包含完成和取消成员的生命周期句柄或操作对象。`ITabStateService` 仅接受 JSON 对象。新增的 `UseCookieService` 和 `UseWebRequestService` 构建器方法是可选的，不会改变未注册这些提供程序的宿主。

## 构建与测试

仓库使用 .NET SDK 9.0.315，默认 CefSharp 版本为 121.3.70。构建和测试其他受支持的 CefSharp 版本时，请在还原、构建、测试和打包命令中传入相同的 `CefSharpVersion` 属性：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 --no-build
# Example: -p:CefSharpVersion=151.3.240 on every command above
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复执行上述 .NET 命令。请将不同平台的 CefSharp 构建产物打包到不同目录。生成的适配器包文件为 `Mzying2001.MonkeySharp.CefSharp.x64.3.0.0.nupkg` 和 `Mzying2001.MonkeySharp.CefSharp.x86.3.0.0.nupkg`：

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -o artifacts/packages/x86
```

WinForms 冒烟测试宿主是基于真实 Chromium 的兼容性验证关卡。它需要 Windows 桌面会话和 CefSharp 原生二进制文件；`Run-E2E.ps1` 会构建、运行并解析 JSON Lines 输出，只有存储、XHR（包括凭据、重定向、二进制和大体积请求体/响应体、重复响应头、流式传输、超时和中止）、通知、标签页、下载、Cookie、webRequest、导航、框架生命周期以及最终资源释放检查全部通过时，验证才会成功：

```powershell
dotnet build Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x64
dotnet run --project Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x64 --no-build

dotnet build Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x86
dotnet run --project Mzying2001.MonkeySharp.CefSharp.SmokeHost/Mzying2001.MonkeySharp.CefSharp.SmokeHost.csproj -c Release -p:Platform=x86 --no-build

./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86
```

向脚本传入 `-CefSharpVersion 151.3.240` 可验证另一组 CefSharp 托管和原生组件。其 JSON Lines 输出包含记录各项 API 布尔结果的 `smoke-summary` 记录，以及 `final-disposal` 记录。冒烟测试宿主使用 `TrustedPageWorld`；它是集成验证，而不是安全沙箱。页面主世界中的代码可以观察并调用已授予的能力。

高级的脚本管理器私有行为有意不纳入兼容性承诺：仅限浏览器扩展的 Cookie 分区、管理器 UI 状态、下载栏展示、通知持久化，以及需要等待 JavaScript 的请求拦截 API，均取决于具体宿主。实现只能公开文档中规定的提供程序契约；缺少某项能力时，必须返回 `MSP006_NOT_SUPPORTED`。

## 许可证

MIT。详见 [LICENSE](LICENSE)。
