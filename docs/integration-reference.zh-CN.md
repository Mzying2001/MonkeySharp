# MonkeySharp 集成参考

[English](integration-reference.md) | **中文**

本文集中说明实现和维护 MonkeySharp 宿主时需要查阅的兼容性、网络 API、宿主服务、迁移及验证细节。安装和最小接入方式请先阅读仓库 [README](../README.zh-CN.md)。

## CefSharp 版本兼容性

同一个适配器二进制文件无法兼容任意 CefSharp 版本。CefSharp 程序集具有强名称，而且公开接口曾在版本间发生变化，例如 `IBrowser.GetFrame(long)` 改为 `GetFrameByIdentifier(string)`，`IFrame.Identifier` 也从 `long` 改为 `string`。

适配器默认使用 `121.3.70`，源码兼容层已针对 `84.4.10`、`121.3.70` 和 `151.3.240` 验证。应用程序应针对实际发布的版本重新构建并测试：

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

适配器 NuGet 包将 `CefSharp.Common` 引用设为私有。使用者需要自行引用匹配的 `CefSharp.WinForms`、`CefSharp.Wpf` 或 `CefSharp.Common` 包，并在 .NET Framework 应用中允许常规绑定重定向。托管包、CEF 原生文件、MonkeySharp 适配器的版本和进程平台必须一致。

## 兼容性配置

`CefSharpHostOptions.Compatibility` 默认为 `LegacyCompatible`。它接受 `GM_getValue` 等旧式授权，按兼容语义公开 `GM_*` 全局对象，并建立有界的同步存储与资源镜像。旧式回调 API 与 `GM.*` 使用相同的认证桥接操作。

需要严格包装器语义和仅 Promise API 名称时使用 `ModernStrict`：

```csharp
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

宿主选择执行期配置，仓库解析器选择元数据配置，因此两处应使用同一个 profile：

```csharp
var parser = new UserScriptMetadataParser(
    new UserScriptMetadataParserOptions
    {
        Profile = UserScriptCompatibilityProfile.ModernStrict,
        AllowHeaderPreamble = false
    });
var repository = new InMemoryUserScriptRepository(parser);
```

## 网络 API

CefSharp 宿主默认提供 `GM.xmlHttpRequest`、`GM.cookie` 和 `GM.webRequest` 服务。设置 `EnableDefaultNetworkServices = false` 可要求显式注入；也可用 `UseHttpRequestService`、`UseCookieService` 和 `UseWebRequestService` 分别替换默认实现。

### HTTP 请求

脚本必须声明 `@grant GM.xmlHttpRequest` 或 `@grant GM_xmlhttpRequest`，并为每个目标声明 `@connect`。方法名不区分大小写，除 `CONNECT`、`TRACE` 和 `TRACK` 外可使用有效的 HTTP token。

请求体支持字符串、`ArrayBuffer`、类型化数组、`DataView`、`Blob`/`File`、`FormData`、`URLSearchParams`、普通对象和数组。对象及数组使用 JSON，`URLSearchParams` 使用表单编码，`FormData` 保留生成的 boundary、文件名和媒体类型。请求体以 64 KiB 分块跨越桥接。

支持 `headers`、`cookie`、`user`、`password`、`anonymous`、`overrideMimeType`、`signal`、`redirect`、`nocache`、`revalidate`、`fetch`、`timeout` 和 `context`。重定向默认为 `follow`，也接受 `error` 和 `manual`。每次重定向都会重新检查授权、`@connect` 和宿主权限策略；跨源重定向会移除显式 Cookie 和授权头。

可以设置 `User-Agent`、`Referer`、`Origin` 和 `Cookie` 等兼容性请求头。MonkeySharp 拒绝 `Host`、`Content-Length`、连接/传输控制头及所有 `Sec-*`、`Proxy-*` 头。`anonymous:true` 禁用已存储的 Cookie 和凭据；`fetch:true` 忽略超时和进度，只报告 readyState 4。

`responseType` 接受 `text`、`json`、`arraybuffer`、`blob` 和 `stream`。非流式响应通过 64 KiB 宿主读取拼装。响应包含 readyState 1 至 4、状态、最终 URL、保留重复字段的原始响应头、响应数据、适用的文本及原始 JavaScript context。HTTP 4xx/5xx 调用 `onload`；网络、权限、序列化和桥接故障调用 `onerror`。

回调包括 `onloadstart`、`onreadystatechange`、`onprogress`、`onuploadprogress`、`onload`、`onerror`、`ontimeout`、`onabort` 和 `onloadend`。`GM.xmlHttpRequest` 返回带 `.abort()` 的 Promise；`GM_xmlhttpRequest` 立即返回中止句柄并通过回调报告结果。`AbortSignal` 和流取消都会传递到宿主。

请求和响应默认不限大小。单方向数据在 256 KiB 以内保留在内存，超过后转存临时文件，并在所有终止路径中删除。请求体传输和长时间运行的 `execute` 不受桥接通用消息大小及普通请求超时限制。

`proxy`、`cookiePartition` 以及作为 URL 的 `Blob`/`File` 返回 `MSP006_NOT_SUPPORTED`。Cookie 分区键属于浏览器扩展管理器状态，CefSharp 公开请求上下文 API 不提供该状态。

### Cookie

声明 `@grant GM.cookie` 或 `@grant GM_cookie`。默认提供程序使用已附加浏览器的请求上下文。省略 `details.url` 时使用当前文档 URL；显式 URL 必须被脚本的 `@match` 或 `@include` 覆盖。Core 校验 domain、path、secure、SameSite、`expirationDate`、`httpOnly` 和第一方字段。

```javascript
const cookies = await GM.cookie.list({ url: location.href });
await GM.cookie.set({ url: location.href, name: "session", value: "ready", path: "/" });
await GM.cookie.delete({ url: location.href, name: "session" });
const listenerId = GM.cookie.addListener({}, change => console.log(change.cause));
```

`GM_cookie.list` 回调签名为 `(cookies, error)`，`set` 和 `delete` 回调签名为 `(error)`；成功时错误值为 `null` 或 `undefined`。`addListener/removeListener` 使用数字 ID。执行、文档、框架或宿主释放时会清理监听器。

### Web Request

声明 `@grant GM.webRequest` 或 `@grant GM_webRequest`。默认内存服务会挂载 `CefSharpWebRequestHandler`。注册使用 Tampermonkey selector/action 合约并返回可移除 handle。字符串 selector 是 URL glob（或 `/regexp/`）；对象 selector 支持字符串或数组形式的 `include`、WebExtension `match` 和 `exclude`。action 支持 `cancel`、静态 HTTP(S) 重定向，或 `{ from, to }` 动态替换。重定向目标会在注册时及实际求值时按脚本 URL 规则检查。JavaScript 回调不会阻塞网络线程。

```javascript
const registration = GM.webRequest([
  { selector: "https://example.com/*", action: "cancel" },
  { selector: { match: "https://example.com/*", exclude: "https://example.com/ignore" },
    action: { from: "/old", to: "/new" } }
], (info, message, details) => {
  console.log(info.url, message, details);
});
registration.remove();
```

`GM_webRequest(rules, listener)` 是同一注册函数的 legacy 别名。listener 使用 `(info, message, details)`，只报告 `cancel` 或 `redirect` 结果。适配器会拒绝已被占用且未复用的请求处理器，而不会静默替换。

## 宿主服务契约

Builder 只能使用一次。应用程序拥有仓库、浏览器以及通过 `UseValueStore`、`UsePermissionPolicy` 或 `Use...Service`/`Use...Provider` 传入的对象。宿主拥有自身引擎、网关、内部提供程序、默认网络服务和默认值存储。传给 `AddApiProvider` 的对象所有权转移给网关，适用时由网关释放。

提供程序要求：

- 同一 `ScriptKey` 的 `IUserScriptValueStore` 操作必须线性一致。提交前取消不产生更改；提交后取消仍成功完成并发出一次通知。
- `IUserScriptDependencyProvider` 负责 `@require` 的可信下载、缓存、HTTPS 策略及可选完整性验证；`ResourceScriptSourceResolver` 保持声明顺序并执行大小限制。
- `LegacyCompatible` 中，缺少依赖提供程序会以 `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE` 跳过声明了 `@require` 的调用；提供程序失败产生 `MSR400_DEPENDENCY_RESOLUTION_FAILED`。`ModernStrict` 中，缺少提供程序时直接使用已安装的主源码。
- `IResourceProvider` 返回事先授权的已声明资源，内容受 `BridgeOptions.MaxResourceBytes` 限制。
- `IHttpRequestService` 在工作开始前接收可重复读取的 `IUserScriptHttpBody` 和观察者；它应先报告元数据再报告正文、遵守 `MaxResponseBytes`、在每次重定向前调用 `RedirectAllowed`，并记录已跟随 URL 供 Core 重新检查 `@connect`。
- 每个宿主支持的请求都会将安装实例、框架、方法、目标摘要和当前能力传给 `IUserScriptPermissionPolicy`。

## 生命周期与诊断

适配器为每个框架文档建立身份，并对每个 `(document, frame, script, run-at)` 组合执行一次。导航、上下文释放、分离和宿主释放都会使能力失效并取消待处理工作。

CefSharp 只能提供尽力而为的 document-start 钩子。相应执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

协议错误使用稳定代码 `MSP001` 至 `MSP010` 以及 `MSP999`。诊断不会包含能力令牌或存储值。默认限制为每个请求 1 MiB、每个响应 1 MiB、每个资源 10 MiB、普通请求 30 秒以及每个文档 64 个待处理请求；通过 `CefSharpHostOptions.Bridge` 配置。

## 迁移说明

版本 3 的宿主服务契约不兼容旧源码。`IHttpRequestService.SendAsync` 在开始前接收 `IUserScriptHttpObserver` 并返回 `IHttpRequestOperation`；请求体是可重复读取的流，响应数据增量传递，重定向授权为异步操作。现有适配器应把旧 task 包装为操作对象，并转发响应开始、数据、进度、完成和中止事件。

通知、标签页和下载服务同样返回包含完成和取消成员的生命周期句柄或操作对象。`ITabStateService` 只接受 JSON 对象。`UseCookieService` 和 `UseWebRequestService` 是可选方法，不会影响未注册这些提供程序的宿主。

旧版一次性传输的 `GM_xmlhttpRequest` 请求和结果现在改为 `create`、`appendBody`、`execute`、`readBody`、`abort`、`release` 生命周期会话。旧版 `ForceUseStrict`/`with(window)` 执行方式改为带有显式 `GM`、`unsafeWindow` 和 `window` 参数的严格 `Function` 作用域。

默认配置已经内置兼容层，不存在单独的传输或 WCF 包。需要保留 v1 数据时必须显式迁移存储键。选择 `ModernStrict` 的应用应把元数据授权更新为规范 `GM.*` 名称。

仅限浏览器扩展的 Cookie 分区、管理器 UI 状态、下载栏展示、通知持久化，以及需要等待 JavaScript 的请求拦截 API 仍属于宿主实现细节，不在兼容性承诺内。宿主应只公开文档规定的提供程序契约；能力缺失时返回 `MSP006_NOT_SUPPORTED`。

## 构建、打包与验证

还原、构建、测试和打包必须使用相同的 `CefSharpVersion` 属性：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。Core 及不同平台适配器应输出到不同目录：

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -o artifacts/packages/x86
```

在 Windows 桌面会话中运行真实 Chromium 验证：

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
```

向 SmokeHost 脚本传入 `-CefSharpVersion 151.3.240` 可验证另一组托管与原生组件。两个验证入口都使用隔离 profile 和 `TrustedPageWorld`；它们是集成测试，不是安全沙箱。
