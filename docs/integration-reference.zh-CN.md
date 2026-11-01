# MonkeySharp 集成参考

[English](integration-reference.md) | **中文**

本文集中说明实现和维护 MonkeySharp 宿主时需要查阅的兼容性、网络 API、宿主服务、迁移及验证细节。安装和最小接入方式请先阅读仓库 [README](../README.zh-CN.md)。[用户脚本使用指南](usage-guide.zh-CN.md) 介绍脚本侧的参数、返回值和示例；本文侧重宿主实现与验证。

## CefSharp 版本兼容性

同一个适配器二进制文件无法兼容任意 CefSharp 版本。CefSharp 程序集具有强名称，而且公开接口曾在版本间发生变化，例如 `IBrowser.GetFrame(long)` 改为 `GetFrameByIdentifier(string)`，`IFrame.Identifier` 也从 `long` 改为 `string`。

适配器默认使用 `121.3.70`，源码兼容层已针对 `84.4.10`、`121.3.70` 和 `151.3.240` 验证。应用程序应针对实际发布的版本重新构建并测试：

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

适配器 NuGet 包将 `CefSharp.Common` 引用设为私有。使用者需要自行引用匹配的 `CefSharp.WinForms`、`CefSharp.Wpf` 或 `CefSharp.Common` 包，并在 .NET Framework 应用中允许常规绑定重定向。托管包、CEF 原生文件、MonkeySharp 适配器的版本和进程平台必须一致。

CI 针对 x64 和 x86 验证 `84.4.10`、`121.3.70`、`151.3.240`。每个矩阵项都会还原、构建并测试 solution，运行 JavaScript 协议测试，再用真实 Chromium 执行 SmokeHost。WPF Demo smoke 保留为默认 `121.3.70` 的 x64 验证。

## 兼容性配置

`CefSharpHostOptions.Compatibility` 默认为 `LegacyCompatible`。默认元数据解析器接受 `GM_getValue` 等旧式授权；执行期配置控制包装器语义、旧式全局对象和同步镜像。旧式回调 API 与 `GM.*` 使用相同的认证桥接操作。

| Profile | 包装器与 API 暴露行为 |
| --- | --- |
| `LegacyCompatible`（默认） | 非严格包装器；顶层 `this` 为 window facade。公开现代 API 和已启用的 `GM_*` 别名，并提供有界的同步存储与资源快照。 |
| `ModernStrict` | 严格包装器；顶层 `this` 为 `undefined`。仅公开现代 API 名称，不提供旧式全局对象。各 API 仍遵循自身返回契约：`GM.addValueChangeListener` 返回数字 ID，`GM.openInTab` 和 `GM.webRequest` 立即返回句柄。 |

宿主选择执行期配置，仓库解析器选择元数据配置，因此两处应使用同一个 profile。下面的示例为可信页面和脚本配置宿主：

```csharp
using Mzying2001.MonkeySharp.CefSharp;
using Mzying2001.MonkeySharp.Core.Compatibility;
using Mzying2001.MonkeySharp.Core.Parsing;
using Mzying2001.MonkeySharp.Core.Repository;

var profile = UserScriptCompatibilityProfile.ModernStrict;
var parser = new UserScriptMetadataParser(
    new UserScriptMetadataParserOptions
    {
        Profile = profile,
        AllowHeaderPreamble = false
    });
var repository = new InMemoryUserScriptRepository(parser);
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions
    {
        TrustedPageWorld = true, // 仅用于应用程序信任的页面和脚本。
        Compatibility = new UserScriptCompatibilityOptions { Profile = profile }
    })
    .Build();
```

`TrustedPageWorld` 独立于兼容性 profile，默认为 `false`。未显式启用时，声明了 `none` 以外显式授权的脚本会被抑制，并产生 `MSR201_BRIDGE_INTEGRITY_REQUIRED`，`ModernStrict` 也不例外。可信模式下，需要桥接的执行会产生 `MSR200_UNVERIFIED_BRIDGE`；严格包装器语义不会创建隔离 JavaScript 世界。详见 [README 安全模型](../README.zh-CN.md#安全模型)。

宿主可通过 `ExposeLegacyGlobals`、`EnableSynchronousStorageMirror` 和 `EnableSynchronousResourceSnapshot` 分别禁用旧式全局对象、存储镜像或资源快照。快照准备失败、不完整或超限时，对应的同步别名不可用，现代方法仍可使用桥接。快照限制及降级诊断见[生命周期与诊断](#生命周期与诊断)。

## 资源完整性与更新检查

`@require` 和 `@resource` 支持十六进制或标准 Base64 编码的 `md5`、`sha256` 摘要。支持 `#algorithm=value` 和 `#algorithm-value`，多个摘要可用逗号或分号分隔，并选择最后一个当前支持的声明。请求和缓存 URL 会移除 fragment。已声明完整性要求时，Core resolver、资源 API 和 Demo HTTP 缓存在资源首次使用前验证原始字节，读取缓存时再次校验。不支持的算法会产生 warning；声明格式错误、声明中没有可用的受支持摘要或摘要不匹配都会严格拒绝加载。未声明完整性要求的资源可以加载，不执行摘要校验。

Demo 管理器提供手动“检查并安装更新”。检查源优先使用 `@updateURL`，下载源优先使用 `@downloadURL`；`@downloadURL none` 禁用检查。候选脚本必须具有相同且非空的 `@name`/`@namespace`，下载版本必须高于当前安装且不低于检查源版本。应用更新保留 ScriptKey、启用状态、安装时间和 GM 存储，只替换源码及来源/更新时间。更新检查使用 Demo 独立 HTTP 客户端，不继承浏览器登录 Cookie；没有后台定时任务，也不通过运行时 bridge 暴露更新 API。

`@include` 和 `@exclude` 支持 `/.../` 包裹的正则，以文化无关方式匹配且执行上限为 100 ms；非法正则会产生 metadata error。`@match http*://host/path` 同时覆盖 HTTP 与 HTTPS。既有 glob 语义和排除规则优先级保持不变。

## 网络 API

CefSharp 宿主默认提供 `GM.xmlHttpRequest`、`GM.cookie` 和 `GM.webRequest` 服务。设置 `EnableDefaultNetworkServices = false` 可要求显式注入；也可用 `UseHttpRequestService`、`UseCookieService` 和 `UseWebRequestService` 分别替换默认实现。

### HTTP 请求

完整参数表、回调契约和脚本示例见 [HTTP 请求使用指南](usage-guide.zh-CN.md#http-请求)。

兼容性状态分为 Core 已实现、需要宿主 service、有意降级/不支持、与 Tampermonkey 存在行为差异。运行时在 CefSharp 页面世界的 MonkeySharp wrapper 中执行；`@sandbox`、`@unwrap`、`@run-in` 和非 page 的 `@inject-into` 会保留并产生诊断，但不会创建隔离世界。

脚本必须声明 `@grant GM.xmlHttpRequest` 或 `@grant GM_xmlhttpRequest`，并为每个目标声明 `@connect`。方法名不区分大小写，除 `CONNECT`、`TRACE` 和 `TRACK` 外可使用有效的 HTTP token。

请求体支持字符串、`ArrayBuffer`、类型化数组、`DataView`、`Blob`/`File`、`FormData`、`URLSearchParams`、普通对象和数组。对象及数组使用 JSON，`URLSearchParams` 使用表单编码，`FormData` 保留生成的 boundary、文件名和媒体类型。请求体以 64 KiB 分块跨越桥接。

支持 `headers`、`cookie`、`user`、`password`、`anonymous`、`overrideMimeType`、`signal`、`redirect`、`nocache`、`revalidate`、`fetch`、`timeout` 和 `context`。重定向默认为 `follow`，也接受 `error` 和 `manual`。每次重定向都会重新检查授权、`@connect` 和宿主权限策略；跨源重定向会移除显式 Cookie 和授权头。

可以设置 `User-Agent`、`Referer`、`Origin` 和 `Cookie` 等兼容性请求头。MonkeySharp 拒绝 `Host`、`Content-Length`、连接/传输控制头及所有 `Sec-*`、`Proxy-*` 头。`anonymous:true` 禁用已存储的 Cookie 和凭据；`fetch:true` 忽略超时和进度，只报告 readyState 4。

`binary: true` 只接受字符串请求体，并将每个 UTF-16 code unit 转换为其低八位；普通字符串仍使用 UTF-8。`responseType` 接受 `text`、`json`、`arraybuffer`、`blob`、`document`、`xml` 和 `stream`。`responseXML: true` 使用页面的 `DOMParser` 解析；document 类型也会把解析结果放入 `response`。没有 DOMParser 时 `responseXML` 为 `null`，不会伪造 XML。非流式响应通过 64 KiB 宿主读取拼装。响应包含 readyState 1 至 4、状态、最终 URL、保留重复字段的原始响应头、响应数据、适用的文本、responseXML 及原始 JavaScript context。HTTP 4xx/5xx 调用 `onload`；网络、权限、序列化和桥接故障调用 `onerror`，错误对象为 `{ error, details, code }`，分类包括 `not_supported`、`not_permitted`、`timeout` 和 `not_succeeded`。

回调包括 `onloadstart`、`onreadystatechange`、`onprogress`、`onuploadprogress`、`onload`、`onerror`、`ontimeout`、`onabort` 和 `onloadend`。`GM.xmlHttpRequest` 返回带 `.abort()` 的 Promise；`GM_xmlhttpRequest` 立即返回中止句柄并通过回调报告结果。`AbortSignal` 和流取消都会传递到宿主。

请求体和响应体的总大小默认不受限。单方向数据在 256 KiB 以内保留在内存，超过后转存临时文件，并在所有终止路径中删除。分块允许正文总量超过单条桥接消息的大小限制，但每条序列化后的分块消息仍必须满足 `BridgeOptions.MaxRequestBytes` 或 `MaxResponseBytes`，包括 JSON 和 Base64 开销。

只有长时间运行的 `execute` 操作豁免 `BridgeOptions.RequestTimeout`；其余会话操作仍使用普通请求超时。所有会话请求（包括 `execute`）都计入 `MaxPendingRequestsPerDocument`。HTTP 请求自身的 `timeout` 独立生效，`fetch` 模式除外。

`proxy`、`cookiePartition`、同步请求以及作为 URL 的 `Blob`/`File` 返回 `MSP006_NOT_SUPPORTED`。Cookie 分区键属于浏览器扩展管理器状态，CefSharp 公开请求上下文 API 不提供该状态。现代和 legacy XHR 输入校验使用同一错误码。

### Cookie

声明 `@grant GM.cookie` 或 `@grant GM_cookie`。默认提供程序使用已附加浏览器的请求上下文。省略 `details.url` 时使用当前文档 URL；显式 URL 必须被脚本的 `@match` 或 `@include` 覆盖。Core 校验 domain、path、secure、SameSite、`expirationDate`、`httpOnly` 和第一方字段。

```javascript
const cookies = await GM.cookie.list({ url: location.href });
await GM.cookie.set({ url: location.href, name: "session", value: "ready", path: "/" });
await GM.cookie.delete({ url: location.href, name: "session" });
const listenerId = GM.cookie.addListener({}, (cookie, cause, removed) => console.log(cookie.name, cause, removed));
```

`GM_cookie.list` 回调签名为 `(cookies, error)`，`set` 和 `delete` 回调签名为 `(error)`；成功时错误值为 `null` 或 `undefined`。`addListener/removeListener` 使用数字 ID，监听回调签名为 `(cookie, cause, removed)`。执行、文档、框架或宿主释放时会清理监听器。

### Web Request

声明 `@grant GM.webRequest` 或 `@grant GM_webRequest`。默认内存服务会挂载 `CefSharpWebRequestHandler`。注册使用 Tampermonkey selector/action 合约并返回可移除 handle。字符串 selector 是 URL glob（或 `/regexp/`）；对象 selector 支持字符串或数组形式的 `include`、WebExtension `match` 和 `exclude`。action 支持 `cancel`、静态 HTTP(S) 重定向，或 `{ from, to }` 动态替换。重定向目标会在注册时及实际求值时按脚本 URL 规则检查。JavaScript 回调不会阻塞网络线程。

静态 `@webRequest` 元数据会被解析并写入 `GM.info.script.webRequest` 供检查，但不会注册到网络服务。脚本需要活动注册时必须使用 `GM.webRequest(...)`。

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

`GM_webRequest(rules, listener)` 是同一注册函数的 legacy 别名。listener 使用 `(info, message, details)`，只报告 `cancel` 或 `redirect` 结果。

只要存在 web request 服务（包括默认服务），`Attach` 就要求浏览器尚未初始化，且 `RequestHandler` 槽位为空或使用 `CefSharpWebRequestHandlerMultiplexer`。已有应用程序处理器时，在附加宿主前组合：

```csharp
var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
requestHandlers.Add(applicationRequestHandler);
browser.RequestHandler = requestHandlers;
host.Attach(browser);
```

适配器会拒绝已被非 multiplexer 处理器占用的槽位。设置 `EnableDefaultNetworkServices = false` 会同时禁用三个默认网络服务；通过 `UseWebRequestService` 注入服务时仍须按上述方式组合处理器。已有渲染处理器还须单独使用 `RenderProcessMessageHandlerMultiplexer`，示例见 [README](../README.zh-CN.md#已有的渲染处理器)。

## 宿主服务契约

Builder 只能使用一次。应用程序拥有仓库、浏览器以及通过 `UseValueStore`、`UsePermissionPolicy` 或 `Use...Service`/`Use...Provider` 传入的对象。宿主拥有自身引擎、网关、内部提供程序、默认网络服务和默认值存储。传给 `AddApiProvider` 的对象所有权转移给网关，适用时由网关释放。

需要持久化安装时，将前述示例中的内存仓库替换为 `PersistentUserScriptRepository`，并由应用程序提供和管理 `IUserScriptRepositoryPersistence`。在使用仓库或附加宿主前完成初始化：

```csharp
using System.Threading;

var repository = new PersistentUserScriptRepository(persistence, parser);
await repository.InitializeAsync(CancellationToken.None);
```

持久化实现必须保留 `ScriptKey`、安装顺序及时间戳，并原子提交变更。提交成功后即使收到取消也应成功完成；仓库变更通知在提交后发出。稳定的键用于维持脚本与 GM 存储的关联。具体的持久化及恢复实现见 [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.md#portable-data-and-recovery)。

提供程序要求：

- 同一 `ScriptKey` 的 `IUserScriptValueStore` 操作必须线性一致。提交前取消不产生更改；提交后取消仍成功完成并发出一次通知。
- 值存储还应实现 `IUserScriptValueSnapshotProvider`，以提供原子的兼容性快照。否则网关会分别读取键和值，并产生 `MSC411_COMPATIBILITY_SNAPSHOT_DEGRADED`。
- `IUserScriptDependencyProvider` 负责 `@require` 的可信下载、缓存、HTTPS 策略及可选完整性验证；`ResourceScriptSourceResolver` 保持声明顺序、验证已声明的完整性，并将依赖原始字节总量限制为 `BridgeOptions.MaxResourceBytes`。
- `LegacyCompatible` 中，缺少依赖提供程序会以 `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE` 跳过声明了 `@require` 的调用；提供程序失败产生 `MSR400_DEPENDENCY_RESOLUTION_FAILED`。`ModernStrict` 中，缺少提供程序时直接使用已安装的主源码。
- `IResourceProvider` 返回事先授权的已声明资源，内容受 `BridgeOptions.MaxResourceBytes` 限制。
- `IHttpRequestService` 在工作开始前接收可重复读取的 `IUserScriptHttpBody` 和观察者；它应先报告元数据再报告正文、遵守可选的 `UserScriptHttpRequest.MaxResponseBytes` 正文上限（`null` 表示不受限）、在每次重定向前调用 `RedirectAllowed`，并记录已跟随 URL 供 Core 重新检查 `@connect`。该正文上限独立于桥接消息上限 `BridgeOptions.MaxResponseBytes`。
- 每个宿主支持的请求都会将安装实例、框架、方法、目标摘要和当前能力传给 `IUserScriptPermissionPolicy`。
- `IUserScriptWindowService` 为精确的 `window.close` 和 `window.focus` 授权提供实现。`CloseAsync` 可以拒绝关闭最后一个标签页；不调用 `UseWindowService` 时两个属性都不可用。`window.onurlchange` 由 Core 提供，报告同文档的 `pushState`、`replaceState`、`popstate` 和 `hashchange` 变化。

交互服务的生命周期契约各不相同：

| 服务入口 | 返回句柄 | 生命周期成员 |
| --- | --- | --- |
| `INotificationService.ShowAsync` | 立即返回 `INotificationHandle` | `Completion`、`Clicked`、`Closed`、`Dispose`；没有独立的 `Abort` 成员。 |
| `ITabService.OpenAsync` | `Task<ITabHandle>` | `TabId`、`Closed`、`OnClose`、`CloseAsync`、`Dispose`；没有 `Completion` 任务。 |
| `IDownloadService.DownloadAsync` | `Task<IDownloadOperation>` | `DownloadId`、`Completion`、`Progress`、`Completed`、`Failed`、`Aborted`、`TimedOut`、`Abort`、`Dispose`。 |

运行时通过句柄事件向脚本发送回调；仅完成 `Completion` 任务不会发送终态回调。提供程序应只发出一次终态事件，并支持执行结束时的幂等清理。释放标签页句柄应解除观察关系；关闭用户标签页是独立的 `CloseAsync` 操作。

JavaScript 下载契约与宿主操作契约不同：`GM.download` 返回的 `Promise<DownloadHandle>` 在下载启动时完成，目前会忽略 `details` 中的回调字段。需要脚本侧进度及终态回调时，应在 `LegacyCompatible` 中使用 `GM_download`。返回值及限制详见[下载使用指南](usage-guide.zh-CN.md#下载)。

## 生命周期与诊断

适配器为每个框架文档建立身份，并对每个 `(document, frame, script, run-at)` 组合执行一次。`@run-at context-menu` 脚本会按当前 frame 匹配规则出现在 CefSharp 原生上下文菜单中，只有用户选择菜单项后才创建一次新的执行。导航、上下文释放、分离和宿主释放都会使能力失效并取消待处理工作。

授权状态具有语义差异：缺少 `@grant` 时不会公开 `GM` 门面；显式 `@grant none` 时只公开 `GM.info`，以及在启用旧式全局对象时公开 `GM_info`。`ModernStrict` 中 `GM_info` 不可用。两个名称同时可用时，引用同一个深度冻结快照，包含规范化元数据、授权、URL 规则、资源、更新状态、handler、sandbox 模式和原始元数据文本。版本化的 `compatibility` 块使用 `contractVersion: 1` 标出固定值、元数据来源、混合来源和扩展字段，并明确 page world 执行边界。

CefSharp 只能提供尽力而为的 document-start 钩子。相应执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

除 `raw` 外的 `@sandbox` 值会产生 `MSR212_UNSUPPORTED_SANDBOX`，`@unwrap` 会产生 `MSR213_UNSUPPORTED_UNWRAP`。这两个声明仍保留在元数据和 `GM.info` 中，但脚本仍在页面世界和 MonkeySharp wrapper 内执行。

协议错误使用稳定代码 `MSP001` 至 `MSP010` 以及 `MSP999`。诊断不会包含能力令牌或存储值。将新建的 `BridgeOptions(...)` 赋给 `CefSharpHostOptions.Bridge` 即可配置限制：

| 选项 | 默认值 | 适用范围 |
| --- | --- | --- |
| `MaxRequestBytes` | 1 MiB | 每条 UTF-8 桥接请求，包括 HTTP 正文分块消息。 |
| `MaxResponseBytes` | 1 MiB | 每条 UTF-8 桥接响应，包括完整的 hello 响应和正文读取结果。 |
| `MaxResourceBytes` | 10 MiB | 每个资源；使用 Builder 的 `ResourceScriptSourceResolver` 时还限制依赖字节总量。 |
| `RequestTimeout` | 30 秒 | 普通桥接请求；HTTP `execute` 豁免。 |
| `MaxPendingRequestsPerDocument` | 64 | 同一文档的并发桥接请求，包括 HTTP `execute`。 |
| `MaxCompatibilityBootstrapBytes` | 1 MiB | 每份序列化后的存储或资源快照；完整的 hello 响应还须满足 `MaxResponseBytes`。 |
| `CompatibilityBootstrapTimeout` | 5 秒 | 每份存储或资源快照的准备时间。 |
| `MaxReplayEntriesPerExecution` | 4096 | 每次执行保留用于拒绝重放的近期请求 ID 数量。 |

兼容性快照诊断区分准备失败、一致性降级和大小限制：

| 诊断 | 含义 |
| --- | --- |
| `MSC410_COMPATIBILITY_BOOTSTRAP_FAILED` | 快照准备失败或超时，对应同步别名不可用。 |
| `MSC411_COMPATIBILITY_SNAPSHOT_DEGRADED` | 值存储不支持原子快照，网关退回分别读取键和值。 |
| `MSC412_COMPATIBILITY_BOOTSTRAP_LIMIT` | 快照超出字节预算并被标为不完整，对应同步别名不可用。 |

## 迁移说明

版本 3 的宿主服务契约不兼容旧源码。`IHttpRequestService.SendAsync` 在开始前接收 `IUserScriptHttpObserver` 并返回 `IHttpRequestOperation`；请求体是可重复读取的流，响应数据增量传递，重定向授权为异步操作。现有适配器应把旧 task 包装为操作对象，并转发响应开始、数据、进度、完成和中止事件。

通知、标签页和下载服务分别返回[宿主服务契约](#宿主服务契约)中列出的生命周期句柄。`ITabStateService` 只接受 JSON 对象。`UseCookieService` 和 `UseWebRequestService` 用于替换默认服务；省略调用时仍启用默认实现。设置 `EnableDefaultNetworkServices = false` 可禁用内置网络服务，再显式注入宿主所需能力的服务。存在活动的 web request 服务时，须按前文说明组合请求处理器。

旧版一次性传输的 `GM_xmlhttpRequest` 请求和结果现在改为 `create`、`appendBody`、`execute`、`readBody`、`abort`、`release` 生命周期会话。旧版 `ForceUseStrict`/`with(window)` 执行方式改为带有显式 `GM`、`unsafeWindow` 和 `window` 参数的 `Function` 作用域。`LegacyCompatible` 使用非严格包装器，顶层 `this` 为 window facade；`ModernStrict` 使用严格包装器，顶层 `this` 为 `undefined`。

默认配置已经内置兼容层，不存在单独的传输或 WCF 包。需要保留 v1 数据时必须显式迁移存储键。选择 `ModernStrict` 的应用应把元数据授权更新为规范 `GM.*` 名称。

仅限浏览器扩展的 Cookie 分区、管理器 UI 状态、下载栏展示、通知持久化，以及需要等待 JavaScript 的请求拦截 API 仍属于宿主实现细节，不在兼容性承诺内。宿主应只公开文档规定的提供程序契约；能力缺失时返回 `MSP006_NOT_SUPPORTED`。

## 构建、打包与验证

CI 已验证的工具链为 Windows Server 2022、.NET SDK `9.0.315` 和 Node.js `24`。这些是已测试版本，不代表最低要求。SDK 需要支持 `MonkeySharp.slnx`；Core 测试目标为 `net8.0`，CefSharp 测试及应用程序目标为 `net462`。真实 Chromium 验证需要 Windows 桌面会话。

还原、构建、测试和打包必须使用相同的 `CefSharpVersion` 属性：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=121.3.70
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。Core 及不同平台适配器应输出到不同目录：

Solution 的两个平台均将 Core 映射到 AnyCPU，产物位于 `bin/Release/<framework>/`。打包 Core 时应使用 `-p:Platform=AnyCPU`，通过 `--no-build` 复用 solution 产物时也应如此。CefSharp 适配器则需要显式指定 x64 或 x86 平台。

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=121.3.70 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=121.3.70 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=121.3.70 -o artifacts/packages/x86
```

在 Windows 桌面会话中运行真实 Chromium 验证：

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 121.3.70
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 121.3.70
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 121.3.70
```

验证全过程应使用同一版本值：`dotnet restore/build/test/pack` 使用 `-p:CefSharpVersion=<版本>`，PowerShell runner 使用 `-CefSharpVersion <版本>`。SmokeHost 应分别对 x64/x86 和 `84.4.10`、`121.3.70`、`151.3.240` 运行；Demo gate 固定为默认版本 `121.3.70` 的 x64。在已有工作区切换版本前，先执行 `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<目标版本>`（位数使用目标平台），避免混用不同 CefSharp 版本的增量构建产物。SmokeHost runner 已使用选定版本执行 restore、clean、build，并核对实际运行的程序集版本和进程位数。两个验证入口都使用隔离 profile 和 `TrustedPageWorld`；它们是集成测试，不是安全沙箱。

### Chromium 兼容性脚本包

SmokeHost 从 `Fixtures` 目录内嵌七个原创 `.user.js`，通过正常仓库安装流程运行。两个动态端口的 loopback HTTP 服务提供全部运行输入。脚本验证 MonkeySharp 公开契约，不包含第三方脚本或 Tampermonkey 差分运行结果。

适配器在匹配脚本前读取当前渲染器的 `location.href`，因为旧版 CefSharp 的上下文创建回调可能仍携带上一次导航的 URL。读取失败时跳过该文档，包括 load fallback 和上下文菜单执行。document-start 仍为 best effort。

| 结果 ID | 必须提供的验证证据 |
| --- | --- |
| `frames` | 主 frame、同源子 frame、不同端口的异源子 frame 实际调用 GM 存储；每文档执行一次、`@noframes`、子 frame 导航及上下文释放。 |
| `spaUrl` | `pushState`、`replaceState`、hash 导航与历史回退；两种 URL 回调、新旧 URL、宿主生命周期、grant 边界和不重复注入。 |
| `download` | 现代 Promise/handle、legacy 回调；真实 HTTP 下载及文件字节校验、产生进度后取消、超时、HTTP 失败、唯一终态及临时文件清理。 |
| `cookie` | 现代/legacy set、list、delete，监听与取消监听、共享 Chromium request context，以及匿名 XHR 不携带 cookie。 |
| `webRequest` | 浏览器原生 fetch 的取消/重定向、回调详情、服务端请求计数、主动移除及导航清理。 |

下载服务仅用于测试：Chromium 执行 GM 调用，宿主服务将本地 HTTP 数据写入隔离临时目录，不测试 Chromium 下载界面。原有存储、XHR、通知、标签页和导航断言仍为必需。

每个场景输出 `compatibility-case` JSON，包含 `caseId`、`success`、具名 `checks` 及失败上下文（`frameId`、`documentId`、`url`、`state`、`error`）。`smoke-summary.compatibility` 必须包含五项成功结果。runner 独立拒绝缺失/重复场景、失败断言、实际位数或 CefSharp 程序集版本不符、未 detach/dispose 和非零退出。轮询截止时间为 15 秒；宿主总超时 90 秒，runner 在 120 秒后终止停滞的进程树。

执行 `powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.CefSharp.SmokeHost/Test-SmokeResults.ps1` 可在不启动浏览器的情况下验证结果解析。脚本同时支持 Windows PowerShell 和 PowerShell 7。

日志保存到 `artifacts/smoke/<version>-<platform>/`（构建步骤有输出时的 `restore.log`、`clean.log`、`build.log`，以及 `stdout.log`、`stderr.log`、`results.jsonl`）；可通过 `-OutputDirectory` 覆盖路径。CI 在失败时也将该目录上传为 `chromium-<version>-<platform>`。定位问题时先查看失败的 `compatibility-case`，再检查相邻的生命周期、console 和 diagnostic 记录。
