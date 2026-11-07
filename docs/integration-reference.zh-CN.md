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

CI 仅在目标分支为 `dev` 或 `main` 的 PR 上触发，针对 x64 和 x86 验证 `84.4.10`、`121.3.70`、`151.3.240`。每个矩阵项都会还原、构建并测试 solution，运行 JavaScript 协议测试，再用真实 Chromium 执行 SmokeHost。WPF Demo smoke 保留为默认 `121.3.70` 的 x64 验证。

## 授权与 API 契约

MonkeySharp 只有一套用户脚本契约，保留两种官方调用形式。
[固定 5.5.1 实测](compatibility/api-contract.md) 决定暴露规则：旧式全局需要精确声明名；
现代存储、资源、菜单和标签数据 API 按家族授权。例如 `GM_getValue` 声明会提供该旧式名称，
以及含 `GM.setValue` 的现代存储家族，但不会提供未声明的 `GM_setValue` 全局。

未声明、显式 none、未知授权和 info-only 保留各自元数据状态。固定对照环境在这些情况下
均提供 `GM_info` 和 `GM.info`。none 不提供 unsafeWindow，其余声明可访问页面 global。
包装器尊重源码自身的 `"use strict"`，调用风格不选择 strict 或执行世界。
顶层 this 是 window 门面；none 按实测使用独立对象 receiver。DOM/content 执行使用每脚本原生隔离世界；raw 保持页面世界。

```csharp
var repository = new InMemoryUserScriptRepository();
var host = new CefSharpUserScriptHostBuilder(repository)
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

TrustedPageWorld 默认关闭；特权执行需要可信桥或宿主显式信任页面。仅 info 的脚本跳过桥接。
旧式同步存储和资源使用有界准备快照。不再提供 profile、别名解析、旧式全局或快照启用开关。
任何调用风格下，require 缺少提供器都会以 `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE` 跳过执行。

`IDownloadService.DownloadMode` 报告后端实际语义。`IsIncognito` 从实际 context 解析私密状态，
`AutomaticallyUpdatesScripts` 报告真实更新策略；未知状态不写入 info。Demo 明确报告其共享普通
context 和仅手动更新策略。`GM.info.version` 是管理器程序集版本，脚本版本位于 `script.version`。
`GM.info.compatibility.capabilities` 记录 API 是否可用及明确的政策、引擎限制原因。

## 资源完整性与更新检查

安装前通过 `new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(dependencyProvider, resourceProvider))` 配置内容服务，持久化仓库接受相同的 preparer。安装、编辑和更新会先完整准备，再原子发布；失败保留原版本。所有 frame 消费捕获的不可变版本，执行时不再调用内容提供程序。依赖共享作用域并保留指令/严格模式语义，段间加入换行和分号。

`ResourceContent` 保留原始字节、MIME、可选 charset 及宿主解码文本。宿主文本优先，否则按 charset 解码，默认 UTF-8；二进制 data URL 使用原始字节。Demo 将准备内容与脚本原子持久化，重载时验证内容摘要，并为格式 2 数据库添加表而不删除数据。已有记录在初始化时一次性准备；内容不可用会明确中止初始化并保留数据库。安装内容服务的可信网络政策与 GM 请求授权分别处理。

默认安装源码/内容总预算为 32 MiB；序列化资源包预算为 64 MiB，必要时按索引分块传输。用户代码只在完整重组后启动，缺块、重复索引或截断都会失败。详见 [P1 实施记录](compatibility/p1-implementation.md)。

`@require` 和 `@resource` 支持十六进制或标准 Base64 编码的 `md5`、`sha1`、`sha256`、`sha384`、`sha512` 摘要。支持 `#algorithm=value` 和 `#algorithm-value`，多个摘要可用逗号或分号分隔，并选择最后一个当前支持的声明。请求和缓存 URL 会移除 fragment。已声明完整性要求时，Core preparer、资源 API 和 Demo HTTP 缓存在资源首次使用前验证原始字节，读取缓存时再次校验。不支持的算法会产生 warning；声明格式错误、声明中没有可用的受支持摘要或摘要不匹配都会严格拒绝加载。未声明完整性要求的资源可以加载，不执行摘要校验。

Demo 管理器提供手动“检查并安装更新”。检查源优先使用 `@updateURL`，下载源优先使用 `@downloadURL`；`@downloadURL none` 禁用检查。候选脚本必须具有相同且非空的 `@name`/`@namespace`，下载版本必须高于当前安装且不低于检查源版本。应用更新保留 ScriptKey、启用状态、安装时间和 GM 存储，只替换源码及来源/更新时间。更新检查使用 Demo 独立 HTTP 客户端，不继承浏览器登录 Cookie；没有后台定时任务，也不通过运行时 bridge 暴露更新 API。

`@include` 和 `@exclude` 支持 `/.../` 包裹的正则，以文化无关方式匹配且执行上限为 100 ms；非法正则会产生 metadata error。`@match http*://host/path` 同时覆盖 HTTP 与 HTTPS。既有 glob 语义和排除规则优先级保持不变。

## 网络 API

CefSharp 宿主默认提供 `GM.xmlHttpRequest`、`GM.cookie` 和 `GM.webRequest` 服务。设置 `EnableDefaultNetworkServices = false` 可要求显式注入；也可用 `UseHttpRequestService`、`UseCookieService` 和 `UseWebRequestService` 分别替换默认实现。

### HTTP 请求

完整参数表、回调契约和脚本示例见 [HTTP 请求使用指南](usage-guide.zh-CN.md#http-请求)。

兼容性状态分为 Core 已实现、需要宿主 service、有意降级/不支持、与 Tampermonkey 存在行为差异。raw/默认/JavaScript 使用页面世界；DOM/content 使用每脚本隔离 context；不支持或冲突的要求会在执行前拒绝。

脚本必须声明 `@grant GM.xmlHttpRequest` 或 `@grant GM_xmlhttpRequest`，并为每个目标声明 `@connect`。方法名不区分大小写，除 `CONNECT`、`TRACE` 和 `TRACK` 外可使用有效的 HTTP token。

请求体支持字符串、`ArrayBuffer`、类型化数组、`DataView`、`Blob`/`File`、`FormData`、`URLSearchParams`、普通对象和数组。对象及数组使用 JSON，`URLSearchParams` 使用表单编码，`FormData` 保留生成的 boundary、文件名和媒体类型。请求体以 64 KiB 分块跨越桥接。

支持 `headers`、`cookie`、`user`、`password`、`anonymous`、`overrideMimeType`、`signal`、`redirect`、`nocache`、`revalidate`、`fetch`、`timeout` 和 `context`。重定向默认为 `follow`，也接受 `error` 和 `manual`。每次重定向都会重新检查授权、`@connect` 和宿主权限策略；跨源重定向会移除显式 Cookie 和授权头。

可以设置 `User-Agent`、`Referer`、`Origin` 和 `Cookie` 等兼容性请求头。MonkeySharp 拒绝 `Host`、`Content-Length`、连接/传输控制头及所有 `Sec-*`、`Proxy-*` 头。`anonymous:true` 禁用已存储的 Cookie 和凭据；`fetch:true` 忽略超时和进度，只报告 readyState 4。

`binary: true` 只接受字符串请求体，并将每个 UTF-16 code unit 转换为其低八位；普通字符串仍使用 UTF-8。`responseType` 接受 `text`、`json`、`arraybuffer`、`blob`、`document`、`xml` 和 `stream`。`responseXML: true` 使用页面的 `DOMParser` 解析；document 类型也会把解析结果放入 `response`。没有 DOMParser 时 `responseXML` 为 `null`，不会伪造 XML。非流式响应通过 64 KiB 宿主读取拼装。响应包含 readyState 1 至 4、状态、最终 URL、保留重复字段的原始响应头、响应数据、适用的文本、responseXML 及原始 JavaScript context。HTTP 4xx/5xx 调用 `onload`；网络、权限、序列化和桥接故障调用 `onerror`，错误对象为 `{ error, details, code }`，分类包括 `not_supported`、`not_permitted`、`timeout` 和 `not_succeeded`。

回调包括 `onloadstart`、`onreadystatechange`、`onprogress`、`onuploadprogress`、`onload`、`onerror`、`ontimeout`、`onabort` 和 `onloadend`。`GM.xmlHttpRequest` 返回带 `.abort()` 的 Promise；`GM_xmlhttpRequest` 立即返回中止句柄并通过回调报告结果。`AbortSignal` 和流取消都会传递到宿主。

请求体、响应体及暂存下载默认分别限制为 256 MiB（BridgeOptions.MaxBinaryTransferBytes）。单方向数据在 256 KiB 以内保留在内存，超过后转存临时文件，并在所有终止路径中删除。分块允许正文总量超过单条桥接消息的大小限制，但每条序列化后的分块消息仍必须满足 `BridgeOptions.MaxRequestBytes` 或 `MaxResponseBytes`，包括 JSON 和 Base64 开销。

长时间运行的 `execute` 和等待流数据的 `readBody` 操作豁免 `BridgeOptions.RequestTimeout`；其余会话操作仍使用普通请求超时。所有会话请求（包括 `execute`）都计入 `MaxPendingRequestsPerDocument`。HTTP 请求自身的 `timeout` 独立生效，`fetch` 模式除外。

`proxy`、`cookiePartition` 和同步请求返回 `MSP006_NOT_SUPPORTED`。Cookie 分区键属于浏览器扩展管理器状态，CefSharp 公开请求上下文 API 不提供该状态。现代和 legacy XHR 输入校验使用同一错误码。

### Cookie

默认服务使用已附加浏览器的共享 CDP 会话及实际 RequestContext。访问前检查授权、宿主策略与脚本 URL 规则，返回后按 host-only/domain/path/secure 再过滤。SameSite 实际映射到 CDP，并读回 session/expiration 状态。set 检查引擎接受及实际身份/属性，delete 仅删除选中身份的准确 domain/path，再验证不存在；不再反射回显输入或提供自有监听。

现代 list 兑现实际 Cookie[]，set/delete 兑现 undefined。旧版 CRUD 返回 undefined，list 回调为 (cookies,error)，修改回调为 (error)。默认 path 是 URL 所在目录。分区及 first-party 参数在副作用前拒绝，后续分区支持独立实施。直接创建服务使用 new CefSharpCookieService(host)，不再使用独立 RequestContext 构造器；自定义 ICookieService 仅实现 CRUD。详见 [Cookie 指南](usage-guide.zh-CN.md#cookie)及[真实证据](compatibility/cookie-findings.md)。

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

适配器会拒绝已被非 multiplexer 处理器占用的槽位。设置 `EnableDefaultNetworkServices = false` 会同时禁用三个默认网络服务；通过 `UseWebRequestService` 注入服务时仍须按上述方式组合处理器。已有渲染处理器还须单独使用 `RenderProcessMessageHandlerMultiplexer`，示例见 [README](../README.zh-CN.md#已有的浏览器处理器)。

## 宿主服务契约

Builder 只能使用一次。应用程序拥有仓库、浏览器以及通过 `UseValueStore`、`UsePermissionPolicy` 或 `Use...Service`/`Use...Provider` 传入的对象。宿主拥有自身引擎、网关、内部提供程序、默认网络服务和默认值存储。传给 `AddApiProvider` 的对象所有权转移给网关，适用时由网关释放。

以下示例假设仓库及服务实例已创建，只需注册应用程序提供的服务。`TrustedPageWorld` 仅适用于可信页面和脚本：

```csharp
var host = new CefSharpUserScriptHostBuilder(repository)
    .UseValueStore(persistentStore)
    .UsePermissionPolicy(permissionPolicy)
    .UseMenuService(menuService)
    .UseNotificationService(notificationService)
    .UseClipboardService(clipboardService)
    .UseTabService(tabService)
    .UseDownloadService(downloadService)
    .UseTabStateService(tabStateService)
    .UseWindowService(windowService)
    .UseHttpRequestService(httpRequestService) // 可选：替换默认服务
    .UseCookieService(cookieService)           // 可选：替换默认服务
    .UseWebRequestService(webRequestService)   // 可选：替换默认服务
    .LogTo(entry => applicationLog.Write(entry.JsonValue))
    .Configure(new CefSharpHostOptions { TrustedPageWorld = true })
    .Build();
```

需要持久化安装时，将前述示例中的内存仓库替换为 `PersistentUserScriptRepository`，并由应用程序提供和管理 `IUserScriptRepositoryPersistence`。在使用仓库或附加宿主前完成初始化：

```csharp
using System.Threading;

var repository = new PersistentUserScriptRepository(persistence, parser);
await repository.InitializeAsync(CancellationToken.None);
```

Demo 使用数据库格式 2，拒绝旧开发数据库，不进行迁移或删除；请备份后手动重建。自定义值存储需采用下方的新类型契约。

持久化实现必须保留 `ScriptKey`、安装顺序及时间戳，并原子提交变更。提交成功后即使收到取消也应成功完成；仓库变更通知在提交后发出。稳定的键用于维持脚本与 GM 存储的关联。具体的持久化及恢复实现见 [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.md#portable-data-and-recovery)。

提供程序要求：

- `IUserScriptValueStore.SetAsync` 接收不可变的 `UserScriptValue`，由 `StoredValue.Value` 返回；仅以 `StoredValue.Exists` 区分键是否存在。使用 `UserScriptValue.FromJson(json)`、`UserScriptValue.Undefined` 或 `UserScriptValue.FromEncoded(encoded)` 构造值。这替代原先的原始 JSON 契约，不保留旧重载。
- 持久化 `EncodedValue`，不得丢失类型信息。编码为：undefined 使用 `u`，布尔使用 `btrue`/`bfalse`，数字使用 `n` 加数字或 `NaN`/`Infinity`/`-Infinity`，字符串使用 `s` 加原文，对象/数组/null 使用 `o` 加规范 JSON。`FromEncoded` 校验并规范化编码，用户对象的属性名不会作为协议标记。
- 同一 `ScriptKey` 的操作必须线性一致。值键允许空字符串，脚本分区 ID 必须非空。提交前取消不产生更改，提交后取消仍成功完成。每个实际提交变更仅发出一次 `ValueChanged`，包括创建或删除已存储的 undefined；相同编码值的重复写入及删除不存在的键不通知。`ListKeysAsync` 保留插入顺序，覆盖不移动，删除再创建移到末尾。
- `IUserScriptValueStore` 必须提供原子 `GetSnapshotAsync`（值及分区水位）、`GetChangesAsync`（从水位读取连续后缀）和 `ApplyAsync`（携带请求/执行来源的原子批次）。先订阅 `ValueChanged`，再获取快照并重放水位后的变更；没有逐键快照回退。内存与 SQLite 每脚本保留 4096 条变更，过期或未来水位要求重新快照。水位属于一个存储服务会话；宿主重启后执行凭据失效。
- 批量写入/删除通过一次 `ApplyAsync` 全量校验和原子提交，失败不保留部分键或通知。事件贯通 mutationId、originExecutionId、方法和每脚本序列。此原子性是 MonkeySharp 存储契约，不宣称 Tampermonkey 保证事务。批量读取使用一次原子快照。`ApplyAsync` 返回带精确水位的 `UserScriptValueCommit`，空操作也有确认水位。JS 门面确认重放后才兑现写入 Promise；已确认镜像与有序 pending 分开维护，本地回调同步触发，提交回声去重。
- `UserScriptPreparer` 在安装前配置给仓库，负责获取和校验依赖/资源，保留依赖顺序和作用域，并在全部成功后原子激活不可变版本。默认源码及内容总预算为 32 MiB。安装或更新失败不会发布新版本；执行不再抓取安装内容。
- 缺少依赖提供程序时以 `MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE` 跳过执行；提供器失败产生 `MSR400_DEPENDENCY_RESOLUTION_FAILED`，不区分调用风格。
- `IResourceProvider` 返回事先授权的已声明资源，内容受 `BridgeOptions.MaxResourceBytes` 限制。
- `IHttpRequestService` 在工作开始前接收可重复读取的 `IUserScriptHttpBody` 和观察者；它应先报告元数据再报告正文、遵守可选的 `UserScriptHttpRequest.MaxResponseBytes` 正文上限（`null` 表示不受限）、在每次重定向前调用 `RedirectAllowed`，并记录已跟随 URL 供 Core 重新检查 `@connect`。该正文上限独立于桥接消息上限 `BridgeOptions.MaxResponseBytes`。
- 每个宿主支持的请求都会将安装实例、框架、方法、目标摘要和当前能力传给 `IUserScriptPermissionPolicy`。
- `IUserScriptWindowService` 为精确的 `window.close` 和 `window.focus` 授权提供实现。`CloseAsync` 可以拒绝关闭最后一个标签页；不调用 `UseWindowService` 时两个属性都不可用。`window.onurlchange` 由 Core 提供，报告同文档的 `pushState`、`replaceState`、`popstate` 和 `hashchange` 变化。

交互服务的生命周期契约各不相同：

| 服务入口 | 返回句柄 | 生命周期成员 |
| --- | --- | --- |
| `INotificationService.Create` | 立即返回 `INotificationHandle` | 未启动句柄：先订阅再 Start；Clicked 后等待 ResolveClick(preventDefault)，再关闭并完成；Dispose 关闭通知。 |
| `ITabService.OpenAsync` | `Task<ITabHandle>` | `TabId`、`Closed`、`OnClose`、`CloseAsync`、`Dispose`；没有 `Completion` 任务。 |
| `IDownloadService.CreateAsync` | `Task<IDownloadOperation>` | `Start`、`Result`、`DownloadId`、`Completion` 及进度/完成/失败/取消/超时事件、`Abort`、`Dispose`。Create 必须返回未启动操作，先订阅所有事件，再调用 Start。 |

通过 `UseDownloadServiceFactory(http => new FileDownloadService(http, selectDestination))` 与 XHR 共用实际 CEF 网络服务及 webRequest 组合。builder 管理工厂创建的可释放服务。selectDestination 是可信宿主策略或 UI，返回绝对 DownloadDestination，取消时返回 null。browser 模式实现保存和冲突选项；native 模式在副作用前拒绝这些选项。Dispose 取消已接受任务；关闭 CEF 前等待 DrainAsync。必须遵守 DownloadRequest.Anonymous 和 RedirectAllowed。每次操作只发一个终态事件，成功 Result 表示已提交的文件；CreateAsync 不得启动网络或 UI。


运行时通过句柄事件向脚本发送回调；仅完成 `Completion` 任务不会发送终态回调。提供程序应只发出一次终态事件，并支持执行结束时的幂等清理。释放标签页句柄应解除观察关系；关闭用户标签页是独立的 `CloseAsync` 操作。

`GM.download` 立即返回带 abort 的完成 Promise，`GM_download` 返回仅含 abort 的句柄；两者均支持回调。最终文件提交后才完成，详情为 `{url, name, loaded}`。返回形状及生命周期策略详见[下载使用指南](usage-guide.zh-CN.md#下载)。

## 生命周期与诊断

`PrepareAsync()` 准备当前附加会话的共享 CDP；`NavigateAsync(url)` 等待准备后导航。
使用该顺序时，浏览器应以 `about:blank` 初始化。`IsLateAttach` 与 `IsLatePreparation`
分别标记附加、准备发生时初始化已完成。这些 API 不提高用户脚本的时序保证。
参见[真实协议实验结论](compatibility/cdp-findings.md)。

适配器为每个框架文档建立身份，并对每个 `(document, frame, script, run-at)` 组合执行一次。`@run-at context-menu` 脚本会按当前 frame 匹配规则出现在 CefSharp 原生上下文菜单中，只有用户选择菜单项后才创建一次新的执行。导航、上下文释放和分离使文档能力失效并取消待处理调用；已接受下载继续作为宿主任务运行，文档回调停止。宿主释放时取消这些任务。

GM.info 和 GM_info 无需单独授权，始终引用同一个深度冻结快照。声明状态与原始授权列表保持不变。
compatibility 块的 contractVersion 为 2，记录真实执行世界、可用 API 及字段来源。管理器版本来自程序集；
下载模式来自服务，私密状态和更新策略未知时省略；不再提供 position/blockers 占位值。

CefSharp 只能提供尽力而为的 document-start 钩子。相应执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

`@sandbox DOM`/`@inject-into content` 选择原生隔离 context。raw/默认/JavaScript 选择页面访问；DOM + unsafeWindow 及未支持的 unwrap 以 MSR215 明确拒绝，不退回错误世界。

协议错误使用稳定代码 `MSP001` 至 `MSP010` 以及 `MSP999`。诊断不会包含能力令牌或存储值。将新建的 `BridgeOptions(...)` 赋给 `CefSharpHostOptions.Bridge` 即可配置限制：

| 选项 | 默认值 | 适用范围 |
| --- | --- | --- |
| `MaxRequestBytes` | 1 MiB | 每条 UTF-8 桥接请求，包括 HTTP 正文分块消息。 |
| `MaxResponseBytes` | 1 MiB | 每条 UTF-8 桥接响应，包括完整的 hello 响应和正文读取结果。 |
| `MaxResourceBytes` | 10 MiB | 单个资源 API 内容；安装内容总量由 `UserScriptPreparer` 单独限制。 |
| `RequestTimeout` | 30 秒 | 普通桥接请求；HTTP `execute` 豁免。 |
| `MaxPendingRequestsPerDocument` | 64 | 同一文档的并发桥接请求，包括 HTTP `execute`。 |
| `MaxCompatibilityBootstrapBytes` | 64 MiB | 序列化快照总预算；存储和资源快照超过单个响应时按索引分块，在用户代码启动前完整重组。 |
| `CompatibilityBootstrapTimeout` | 5 秒 | 每份存储或资源快照的准备时间。 |
| `MaxReplayEntriesPerExecution` | 4096 | 每次执行保留用于拒绝重放的近期请求 ID 数量。 |

兼容性快照诊断区分准备失败、一致性降级和大小限制：

| 诊断 | 含义 |
| --- | --- |
| `MSC410_COMPATIBILITY_BOOTSTRAP_FAILED` | 快照准备失败或超时，对应同步别名不可用。 |

## 桥协议

当前仅支持桥协议 2，旧版本和未知版本均被拒绝，不保留兼容传输。必要的 CEF 版本适配、
值编码和持久化安装身份保持有效。

## 构建、打包与验证

[Tampermonkey 契约清单](compatibility/api-contract.md) 区分目标行为与版本化实测证据。
运行 `scripts/compatibility/Run-Baseline.ps1` 可清理并验证 84/x64 基线（含 WPF Demo）。
CI 覆盖 84/x64 和 121/x64 的 Demo；报告核验实际程序集版本与进程架构。

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

验证全过程应使用同一版本值：`dotnet restore/build/test/pack` 使用 `-p:CefSharpVersion=<版本>`，PowerShell runner 使用 `-CefSharpVersion <版本>`。SmokeHost 应分别对 x64/x86 和 `84.4.10`、`121.3.70`、`151.3.240` 运行；Demo gate 覆盖 x64 的 `84.4.10` 和 `121.3.70`。在已有工作区切换版本前，先执行 `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<目标版本>`（位数使用目标平台），避免混用不同 CefSharp 版本的增量构建产物。SmokeHost runner 已使用选定版本执行 restore、clean、build，并核对实际运行的程序集版本和进程位数。两个验证入口都使用隔离 profile 和 `TrustedPageWorld`；它们是集成测试，不是安全沙箱。

### Chromium 兼容性脚本包

SmokeHost 从 `Fixtures` 目录内嵌七个原创 `.user.js`，通过正常仓库安装流程运行。两个动态端口的 loopback HTTP 服务提供全部运行输入。脚本验证 MonkeySharp 公开契约，不包含第三方脚本或 Tampermonkey 差分运行结果。

适配器在匹配脚本前读取当前渲染器的 `location.href`，因为旧版 CefSharp 的上下文创建回调可能仍携带上一次导航的 URL。读取失败时跳过该文档，包括 load fallback 和上下文菜单执行。document-start 仍为 best effort。

| 结果 ID | 必须提供的验证证据 |
| --- | --- |
| `frames` | 主 frame、同源子 frame、不同端口的异源子 frame 实际调用单键和批量 GM 存储，覆盖全部六个现代/旧式批量入口、带类型的 undefined、监听返回约定、跨实例通知及导航快照；每文档执行一次、`@noframes`、子 frame 导航及上下文释放。 |
| `spaUrl` | `pushState`、`replaceState`、hash 导航与历史回退；两种 URL 回调、新旧 URL、宿主生命周期、grant 边界和不重复注入。 |
| `download` | 现代 Promise/handle、legacy 回调；真实 HTTP 下载及文件字节校验、产生进度后取消、超时、HTTP 失败、唯一终态及临时文件清理。 |
| `cookie` | 现代/legacy set、list、delete，监听与取消监听、共享 Chromium request context，以及匿名 XHR 不携带 cookie。 |
| `webRequest` | 浏览器原生 fetch 的取消/重定向、回调详情、服务端请求计数、主动移除及导航清理。 |

下载服务仅用于测试：Chromium 执行 GM 调用，宿主服务将本地 HTTP 数据写入隔离临时目录，不测试 Chromium 下载界面。原有存储、XHR、通知、标签页和导航断言仍为必需。

每个场景输出 `compatibility-case` JSON，包含 `caseId`、`success`、具名 `checks` 及失败上下文（`frameId`、`documentId`、`url`、`state`、`error`）。`smoke-summary.compatibility` 必须包含五项成功结果。runner 独立拒绝缺失/重复场景、失败断言、实际位数或 CefSharp 程序集版本不符、未 detach/dispose 和非零退出。轮询截止时间为 15 秒；宿主总超时 90 秒，runner 在 120 秒后终止停滞的进程树。

执行 `powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.CefSharp.SmokeHost/Test-SmokeResults.ps1` 可在不启动浏览器的情况下验证结果解析。脚本同时支持 Windows PowerShell 和 PowerShell 7。

日志保存到 `artifacts/smoke/<version>-<platform>/`（构建步骤有输出时的 `restore.log`、`clean.log`、`build.log`，以及 `stdout.log`、`stderr.log`、`results.jsonl`）；可通过 `-OutputDirectory` 覆盖路径。CI 在失败时也将该目录上传为 `chromium-<version>-<platform>`。定位问题时先查看失败的 `compatibility-case`，再检查相邻的生命周期、console 和 diagnostic 记录。

## 已准备的执行条件

`PreparedScriptRevision.UrlRules` 在激活前编译 URL 规则和可重复的 run-in 选择器，并跨 frame 复用，缓存随元数据生命周期释放。include glob 的 scheme/host 通配有边界，? 为普通字符，整体忽略大小写；正则支持公布的 ECMAScript 子集并限时 100 ms。超时以 MSR214_URL_RULE_TIMEOUT 拒绝该脚本。固定对照覆盖 IPv6、IDN、显式/通配端口、query/hash 及百分号编码的非保留字符。

`RunIn` 为只读选择器列表。`CefSharpHostOptions.IsIncognito` 必须反映宿主实际拥有的 RequestContext；未知环境会以 MSR211_RUN_IN_UNAVAILABLE 拒绝受限执行。容器选择报告 MSM083_UNSUPPORTED_CONTAINER，未知选择器是安装错误。手动 context-menu 忽略 include/exclude，保留 match/exclude-match、noframes、隐私和信任检查；这些筛选不创建私密上下文。

固定管理器中，@match 的 Unicode 主机声明被保留但不匹配规范化后的浏览器主机；应使用 Punycode。安装会以 MSM084_UNSUPPORTED_IDN_MATCH 提示该无效声明。Punycode 规则可匹配 IDN 浏览器 URL。

## 原生隔离世界与生命周期

DOM/content 需求使用每安装脚本的独立 CDP 世界，身份来自原生 frame/loader/context 事件，派发绑定脚本及版本，源码/通知通过固定 context 代际的 remote object 投递。页面绑定拒绝隔离执行证明。raw/默认/Chromium JavaScript 需求仍使用页面世界和明确的 TrustedPageWorld 政策；DOM + unsafeWindow 冲突及未支持的 unwrap 会在源码启动前以 MSR215 拒绝，不静默退回 raw。

document-idle 在 DOMContentLoaded 后调度，不等待缓慢资源的 load。沙箱 window 按脚本私有地回放 DOMContentLoaded/load 缓存，回放事件 isTrusted 为 false；idle 不回放更早的 DOMContentLoaded。监听支持 once、移除及 abort，执行结束清理运行时拥有的监听；直接在页面 DOM 上注册的监听由用户代码管理。源码、DOM、样式的 CSP 效果分别验证，不全站关闭 CSP。

完整 GM 的 document-start 仍为 best effort，显式保证要求会拒绝。OOPIF 不支持时明确失败且没有 raw 回退；BFCache 缓存执行撤销，需要新导航。isolated 的历史 page-frame webRequest 不支持，子 frame 的隔离手动菜单尚未与原生 CEF 菜单 frame 映射。详见[执行证据和边界](compatibility/execution-worlds.md)。

## 交互式授权

需要同意交互的宿主跨标签共享 `InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), decisionStore, prompt)`。同意不扩大声明范围：XHR 检查 connect，Cookie 检查 match/include，下载使用宿主政策。首请求及重定向进入同一决策路径。宿主显式 Allow/Deny 优先；Prompt 才读取记忆决定并异步交互。未配置解析器的 Prompt 是宿主配置错误，不等于静默拒绝；无 UI 宿主显式使用 RejectUserScriptPermissionPrompt。

记忆键是稳定安装身份、权限类型及 HTTP(S) origin，不保存凭据、path/query。允许一次仅对当前合并等待批次有效，不能授权后续请求。单个等待者取消不影响其他文档的同批请求，全部取消才关闭提示。导航取消交互等待，授权结束后再次检查安装版本及启用状态。

普通调用超时在授权后开始；CEF 传输与下载的截止计时在重定向权限等待时暂停并恢复剩余预算。Demo 添加 SQLite 权限表而不删除原数据，使用真实异步窗口，并提供权限按钮及按脚本撤销。撤销取消等待并刷新执行状态；WPF E2E 操作真实按钮。

### 二进制传输与流完成

文本按声明字符集增量解码，保留跨分块的多字节字符；HTML MIME 使用 HTML 解析器，无效 XML 返回 null。显式 redirect 或 anonymous 会启用 fetch 语义，忽略传输 timeout/进度且只报告 readyState 4。HTTP stream 的 Promise 在响应头和流可用时兑现，后续失败通过流传播；onload/onloadend 随网络完成触发。renderer 每次按需拉取最多 64 KiB，宿主在 256 KiB 后转存磁盘，并受总预算约束。这限制桥接队列，但 CEF URLRequest 本身不能暂停网络源。暂存流在 EOF、取消或执行结束时释放；非流响应仍在 renderer 内组装。进度回调不保证逐步 responseText。

Blob/File 下载逐块等待宿主确认，再使用共用的保存、取消、临时文件和原子提交路径。provider 在终态前持有 DownloadRequest.Body，自定义下载服务应读取该字节源，不应通过 HTTP 访问 blob 标识。失败或导航清理未启动的暂存；已接受下载可在文档结束后继续，回调已分离。context 保留本地原始对象；固定 TM 5.5.1 对照会跨扩展边界复制对象，因此不声称引用身份一致。

### 页面辅助方法与稳定标签服务

addStyle/addElement 在本地执行：旧版返回节点，现代返回节点的 Promise。默认父节点优先 head，缺失时使用 documentElement；页面 CSP 仍生效。GM.log 将原始多参数交给本地 console，仅向 LogTo 发送有界、尽力投递的 JSON 快照。

OpenTabRequest 支持相邻 Insert 和优先使用的绝对 InsertIndex。服务应报告实际 Closed 状态和一次关闭事件；renderer 客户关联 ID 覆盖确认前关闭。脚本句柄只有 close/closed/onclose，close/window.close/window.focus 返回 undefined。ITabStateService 必须绑定跨文档稳定的宿主标签身份，并按 ScriptKey 隔离数据。Demo 的共享 SQLite 服务绑定 BrowserTabViewModel.TabId，关闭删除，启动清理历史状态；WPF 窗口政策拒绝脚本关闭最后标签。AutomaticallyUpdatesScripts 应反映真实宿主策略；Demo 为 false，没有自动调度器，未知策略省略 info 字段。
