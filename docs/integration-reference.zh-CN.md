# MonkeySharp 集成参考

[English](integration-reference.md) | **中文**

本文集中说明实现和维护 MonkeySharp 宿主时需要查阅的兼容性、网络 API、宿主服务及验证细节。安装和最小接入方式请先阅读仓库 [README](../README.zh-CN.md)。[用户脚本使用指南](usage-guide.zh-CN.md) 介绍脚本侧的参数、返回值和示例；本文侧重宿主实现与验证。

## CefSharp 版本兼容性

同一个适配器二进制文件无法兼容任意 CefSharp 版本。CefSharp 程序集具有强名称，而且公开接口曾在版本间发生变化，例如 `IBrowser.GetFrame(long)` 改为 `GetFrameByIdentifier(string)`，`IFrame.Identifier` 也从 `long` 改为 `string`。

适配器默认使用 `151.3.240`，源码兼容层已针对 `84.4.10`、`121.3.70` 和 `151.3.240` 验证。应用程序应针对实际发布的版本重新构建并测试：

```powershell
dotnet restore Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
```

适配器 NuGet 包将 `CefSharp.Common` 引用设为私有。使用者需要自行引用匹配的 `CefSharp.WinForms`、`CefSharp.Wpf` 或 `CefSharp.Common` 包，并在 .NET Framework 应用中允许常规绑定重定向。托管包、CEF 原生文件、MonkeySharp 适配器的版本和进程平台必须一致。

各项目应保持相同的 `CefSharpVersion`，命令及 CI 矩阵见[构建与验证](#构建打包与验证)。

## 授权与 API 契约

现代 `GM.*` 方法与旧式 `GM_*` 全局共享一套契约。旧式全局需要精确声明；现代存储、资源、菜单和标签数据方法按家族授权。例如 `GM_getValue` 同时允许现代 `GM.setValue`，但不会公开未声明的 `GM_setValue` 全局。详见[脚本侧授权规则](usage-guide.zh-CN.md#execution-grants-and-compatibility)。

页面世界中包含已识别的 info 以外授权的执行需要 `TrustedPageWorld`，其默认值为 false；显式 DOM/content 执行改用经过验证的隔离传输。两种世界仍受授权声明、宿主服务及权限策略约束。[README 安全模型](../README.zh-CN.md#安全模型)说明何时启用信任，[原生隔离世界](#原生隔离世界与生命周期)说明适配器边界。

`GM.info` 和 `GM_info` 无需授权或桥接。`IDownloadService.DownloadMode`、`CefSharpHostOptions.IsIncognito` 和 `AutomaticallyUpdatesScripts` 提供真实服务、上下文和更新策略；未知状态省略。Demo 报告每个标签的实际普通/私密上下文及仅手动更新策略。管理器版本为 `GM.info.version`，脚本版本为 `GM.info.script.version`。

`UserScriptExecutionEnvironment` 保留自定义 `capabilityStatus` 键，包括细分能力。仅报告宿主能够证明的事实，主 API 可用不代表所有子功能均受支持。字段及适配器能力键见[信息参考](usage-guide.zh-CN.md#information-and-logging)。

## 宿主服务契约

Builder 只能使用一次。应用拥有仓库、浏览器及通过 `UseValueStore`、`UsePermissionPolicy` 或 `Use...Service` 传入的对象。宿主拥有自身引擎、网关、内部提供程序、默认服务和值存储。传给 `AddApiProvider` 的对象所有权转移给网关，适用时由网关释放；下载工厂创建的服务归宿主所有。

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

持久化实现必须保留 `ScriptKey`、安装顺序及时间戳，并原子提交变更。提交成功后即使收到取消也应成功完成；仓库变更通知在提交后发出。稳定的键用于维持脚本与 GM 存储的关联。具体的持久化及恢复实现见 [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md#便携数据与恢复)。

提供程序要求：

- `IUserScriptValueStore.SetAsync` 接收不可变的 `UserScriptValue`，由 `StoredValue.Value` 返回；仅以 `StoredValue.Exists` 区分键是否存在。使用 `UserScriptValue.FromJson(json)`、`UserScriptValue.Undefined` 或 `UserScriptValue.FromEncoded(encoded)` 构造值。
- 持久化 `EncodedValue`，不得丢失类型信息。编码为：undefined 使用 `u`，布尔使用 `btrue`/`bfalse`，数字使用 `n` 加数字或 `NaN`/`Infinity`/`-Infinity`，字符串使用 `s` 加原文，对象/数组/null 使用 `o` 加规范 JSON。`FromEncoded` 校验并规范化编码，用户对象的属性名不会作为协议标记。
- 同一 `ScriptKey` 的操作必须线性一致。值键允许空字符串，脚本分区 ID 必须非空。提交前取消不产生更改，提交后取消仍成功完成。每个实际提交变更仅发出一次 `ValueChanged`，包括创建或删除已存储的 undefined；相同编码值的重复写入及删除不存在的键不通知。`ListKeysAsync` 保留插入顺序，覆盖不移动，删除再创建移到末尾。
- `IUserScriptValueStore` 必须提供原子 `GetSnapshotAsync`（值及分区水位）、`GetChangesAsync`（从水位读取连续后缀）和 `ApplyAsync`（携带请求/执行来源的原子批次）。先订阅 `ValueChanged`，再获取快照并重放水位后的变更；没有逐键快照回退。内存与 SQLite 每脚本保留 4096 条变更，过期或未来水位要求重新快照。水位属于一个存储服务会话；宿主重启后执行凭据失效。
- 批量写入/删除通过一次 `ApplyAsync` 全量校验和原子提交，失败不保留部分键或通知。事件贯通 mutationId、originExecutionId、方法和每脚本序列。此原子性是 MonkeySharp 存储契约，不宣称 Tampermonkey 保证事务。批量读取使用一次原子快照。`ApplyAsync` 返回带精确水位的 `UserScriptValueCommit`，空操作也有确认水位。JS 门面确认重放后才兑现写入 Promise；已确认镜像与有序 pending 分开维护，本地回调同步触发，提交回声去重。
- 每个宿主支持的请求都会将安装实例、框架、方法、目标摘要和当前能力传给 `IUserScriptPermissionPolicy`。
- `IUserScriptWindowService` 为精确的 `window.close` 和 `window.focus` 授权提供实现。`CloseAsync` 可以拒绝关闭最后一个标签页；不调用 `UseWindowService` 时两个属性都不可用。`window.onurlchange` 由 Core 提供，报告同文档的 `pushState`、`replaceState`、`popstate` 和 `hashchange` 变化。

`IUserScriptDependencyProvider` 和 `IResourceProvider` 向仓库的 `UserScriptPreparer` 提供安装内容，不在每次执行时调用。缺少提供器或获取/校验失败会在发布前使安装或更新失败；资源 API 读取已准备版本。详见[准备与完整性](#资源完整性与更新检查)。

`IMenuService.RegisterAsync` 接收 `Action<MenuCommandInvocation>`。`MenuCommandRequest.OriginFrame` 标识已认证的订阅文档，`ClickedFrame` 要求仅向匹配且经宿主证明的当前 frame 派发。仅填写实际观测到的输入字段。通过 `host.ResolveMenuFrame(nativeFrame, request)` 解析原生 frame；null 表示未知或已过期，不派发定向调用。Demo 弹出菜单没有 frame 身份。该 GM 回调契约与 `@run-at context-menu` 源码执行不同，详见[菜单语义及证据](compatibility/host-interactions.md#menus)。

交互服务的生命周期契约各不相同：

| 服务入口 | 返回句柄 | 生命周期成员 |
| --- | --- | --- |
| `INotificationService.Create` | 立即返回 `INotificationHandle` | 未启动句柄：先订阅再 Start；Clicked 后等待 ResolveClick(preventDefault)，再关闭并完成；Dispose 关闭通知。 |
| `ITabService.OpenAsync` | `Task<ITabHandle>` | `TabId`、`Closed`、`OnClose`、`CloseAsync`、`Dispose`；没有 `Completion` 任务。 |
| `IDownloadService.CreateAsync` | `Task<IDownloadOperation>` | `Start`、`Result`、`DownloadId`、`Completion` 及进度/完成/失败/取消/超时事件、`Abort`、`Dispose`。Create 必须返回未启动操作，先订阅所有事件，再调用 Start。 |

通过 `UseDownloadServiceFactory(http => new FileDownloadService(http, selectDestination))` 与 XHR 共用实际 CEF 网络服务及 webRequest 组合。builder 管理工厂创建的可释放服务。selectDestination 是可信宿主策略或 UI，返回绝对 DownloadDestination，取消时返回 null。browser 模式实现保存和冲突选项；native 模式在副作用前拒绝这些选项。Dispose 取消已接受任务；关闭 CEF 前等待 DrainAsync。必须遵守 DownloadRequest.Anonymous 和 RedirectAllowed。每次操作只发一个终态事件，成功 Result 表示已提交的文件；CreateAsync 不得启动网络或 UI。

运行时通过句柄事件向脚本发送回调；仅完成 `Completion` 任务不会发送终态回调。提供程序应只发出一次终态事件，并支持执行结束时的幂等清理。释放标签页句柄应解除观察关系；关闭用户标签页是独立的 `CloseAsync` 操作。

### 页面辅助方法与稳定标签服务

`GM.addStyle`/`GM.addElement` 在本地执行并遵循页面 CSP，脚本签名见 [DOM 指南](usage-guide.zh-CN.md#dom-helpers)。`GM.log` 在本地保留原始 console 参数，仅向 `LogTo` 发送有界、尽力投递的 JSON 快照。

`OpenTabRequest` 支持相邻 `Insert` 和绝对 `InsertIndex`（后者优先）。`ITabHandle` 须报告实际 `Closed` 状态及一次关闭事件；renderer 关联覆盖确认前关闭。释放句柄仅解除观察，`CloseAsync` 才是关闭用户标签的独立操作。

`ITabStateService` 将值绑定到跨文档稳定的宿主标签身份，并按 `ScriptKey` 隔离。Demo 将 SQLite 状态绑定到 `BrowserTabViewModel.TabId`，关闭删除，启动清理历史状态；窗口策略拒绝关闭最后标签。同一执行的标签保存/读取按调用顺序提交，保存先捕获 JSON 对象。脚本约定见[标签页状态](usage-guide.zh-CN.md#tabs-and-tab-state)。

## 资源完整性与更新检查

安装前通过 `new InMemoryUserScriptRepository(preparer: new UserScriptPreparer(dependencyProvider, resourceProvider))` 配置内容服务，持久化仓库接受相同的 preparer。安装、编辑和更新会先完整准备，再原子发布；失败保留原版本。所有 frame 消费捕获的不可变版本，执行时不再调用内容提供程序。依赖共享作用域并保留指令/严格模式语义，段间加入换行和分号。

`ResourceContent` 保留原始字节、MIME、可选 charset 及宿主解码文本。宿主文本优先，否则按 charset 解码，默认 UTF-8；二进制 data URL 使用原始字节。Demo 将准备内容与脚本原子持久化，重载时验证内容摘要，并为格式 2 数据库添加表而不删除数据。已有记录在初始化时一次性准备；内容不可用会明确中止初始化并保留数据库。安装内容服务的可信网络政策与 GM 请求授权分别处理。

默认安装源码/内容总预算为 32 MiB；序列化资源包预算为 64 MiB，必要时按索引分块传输。用户代码只在完整重组后启动，缺块、重复索引或截断都会失败。详见 [资源与存储证据](compatibility/README.md#storage-and-resources)。

`@require` 和 `@resource` 支持十六进制或标准 Base64 编码的 `md5`、`sha1`、`sha256`、`sha384`、`sha512` 摘要。支持 `#algorithm=value` 和 `#algorithm-value`，多个摘要可用逗号或分号分隔，并选择最后一个当前支持的声明。请求和缓存 URL 会移除 fragment。已声明完整性要求时，Core preparer、资源 API 和 Demo HTTP 缓存在资源首次使用前验证原始字节，读取缓存时再次校验。不支持的算法会产生 warning；声明格式错误、声明中没有可用的受支持摘要或摘要不匹配都会严格拒绝加载。未声明完整性要求的资源可以加载，不执行摘要校验。

Demo 管理器提供手动“检查并安装更新”。检查源优先使用 `@updateURL`，下载源优先使用 `@downloadURL`；`@downloadURL none` 禁用检查。候选脚本必须具有相同且非空的 `@name`/`@namespace`，下载版本必须高于当前安装且不低于检查源版本。应用更新保留 ScriptKey、启用状态、安装时间和 GM 存储，只替换源码及来源/更新时间。更新检查使用 Demo 独立 HTTP 客户端，不继承浏览器登录 Cookie；没有后台定时任务，也不通过运行时 bridge 暴露更新 API。

<a id="storage-snapshots-and-listeners"></a>

## 存储快照与监听

宿主先订阅变更，再原子获取每脚本的值和水位。renderer 完整重组带索引的快照分块，并重放水位后的变更，之后才启动用户代码。序列按脚本和存储会话维护；每分区保留 4096 条变更，历史过期要求新快照，不伪造丢失的监听事件。

renderer 将已确认镜像与有序 pending 变更分开维护，同步读取组合后的镜像。本地监听在 setter 返回前执行；现代写入 Promise 等待宿主提交及重放确认。失败写入被移除，再重放后续 pending，避免旧失败覆盖较新的本地或远端值。来源执行的提交回声去重，远端变更通知一次。重放历史丢失时恢复当前值并报告 MSC414_STORAGE_REPLAY_RESET，无法恢复已经丢失的历史回调。监听注册、移除立即在本地生效；执行及文档结束清理运行时拥有的监听。

自定义存储必须遵守[宿主服务契约](#宿主服务契约)中的原子快照及批量要求。这是 MonkeySharp 的内部一致性保证，不宣称 Tampermonkey 事务或相同 IPC 延迟。详见[存储证据](compatibility/README.md#storage-and-resources)。

## 已准备的执行条件

`PreparedScriptRevision.UrlRules` 在激活前编译 URL 规则和可重复的 run-in 选择器，并跨 frame 复用，缓存随元数据生命周期释放。include glob 的 scheme/host 通配有边界，? 为普通字符，整体忽略大小写；正则支持公布的 ECMAScript 子集并限时 100 ms。超时以 MSR214_URL_RULE_TIMEOUT 拒绝该脚本。固定对照覆盖 IPv6、IDN、显式/通配端口、query/hash 及百分号编码的非保留字符。

`RunIn` 为只读选择器列表。`CefSharpHostOptions.IsIncognito` 必须反映宿主实际拥有的 RequestContext；未知环境会以 MSR211_RUN_IN_UNAVAILABLE 拒绝受限执行。容器选择报告 MSM083_UNSUPPORTED_CONTAINER，未知选择器是安装错误。手动 context-menu 忽略 include/exclude，保留 match/exclude-match、noframes、隐私和信任检查；这些筛选不创建私密上下文。

固定管理器中，@match 的 Unicode 主机声明被保留但不匹配规范化后的浏览器主机；应使用 Punycode。安装会以 MSM084_UNSUPPORTED_IDN_MATCH 提示该无效声明。Punycode 规则可匹配 IDN 浏览器 URL。

## 交互式授权

需要同意交互的宿主跨标签共享 `InteractiveUserScriptPermissionPolicy(new SensitiveUserScriptPermissionsPolicy(), decisionStore, prompt)`。同意不扩大声明范围：XHR 检查 connect，Cookie 检查 match/include，下载使用宿主政策。首请求及重定向进入同一决策路径。宿主显式 Allow/Deny 优先；Prompt 才读取记忆决定并异步交互。未配置解析器的 Prompt 是宿主配置错误，不等于静默拒绝；无 UI 宿主显式使用 RejectUserScriptPermissionPrompt。

记忆键是稳定安装身份、权限类型及 HTTP(S) origin，不保存凭据、path/query。允许一次仅对当前合并等待批次有效，不能授权后续请求。单个等待者取消不影响其他文档的同批请求，全部取消才关闭提示。导航取消交互等待，授权结束后再次检查安装版本及启用状态。

普通调用超时在授权后开始；CEF 传输与下载的截止计时在重定向权限等待时暂停并恢复剩余预算。Demo 添加 SQLite 权限表而不删除原数据，使用真实异步窗口，并提供权限按钮及按脚本撤销。撤销取消等待并刷新执行状态；WPF E2E 操作真实按钮。

## 网络 API

适配器默认提供 HTTP、Cookie 和 webRequest 服务，使用已附加浏览器的实际 RequestContext。设置 `EnableDefaultNetworkServices = false` 可省略全部三个默认服务，或通过 `UseHttpRequestService`、`UseCookieService`、`UseWebRequestService` 分别替换。

### HTTP 请求

[HTTP 请求使用指南](usage-guide.zh-CN.md#http-requests)集中说明参数、返回值、回调和脚本示例。默认服务使用 CEF URLRequest，而非页面 fetch。普通请求使用浏览器适用的 Cookie/凭据，匿名请求禁用它们。显式 `cookie`、`headers.Cookie`、请求 `cookiePartition`、代理和同步请求在网络副作用前拒绝。详见[网络范围](compatibility/network.md)及[显式 Cookie 限制](compatibility/cookies.md#explicit-request-cookies)。

自定义 `IHttpRequestService` 在工作开始前接收可重复读取的 `IUserScriptHttpBody` 和观察者。先报告元数据再报告正文，遵守 `UserScriptHttpRequest.MaxResponseBytes`（`null` 表示不限），在每次重定向前调用 `RedirectAllowed`，并记录已跟随 URL 供 Core 重新检查 `@connect`。该正文上限不同于单条消息的 `BridgeOptions.MaxResponseBytes`。跨源重定向移除 Authorization。自定义显式 Cookie 支持需独立验证合并语义。

### 二进制传输与流完成

请求体、响应体及暂存下载默认各有 256 MiB 总预算（`BridgeOptions.MaxBinaryTransferBytes`），独立于单条消息限制。单个桥接分块最多 64 KiB，JSON/Base64 开销仍须满足消息预算。宿主暂存前 256 KiB 保留内存，之后转入临时文件；非流式响应仍在 renderer 内存中组装。

ReadableStream 请求体在授权后才获取 reader，经逐块确认的 `appendBody` 暂存完成后再 `execute`。`IUserScriptHttpBody` 可为重定向重新打开完整暂存，无需重读源流。CEF 只接受一个 PostData element：小正文使用 bytes，大正文使用临时文件，终态释放。这是暂存后上传，不提供 renderer 源到网络的实时背压。

非 fetch 的 text/JSON/document/XML 进度提供累计解码的 `responseText`；text 同时提供累计 `response`。同一解码器保留跨分块的多字节字符，JSON/document 仍在完成时解析。二进制/stream 不提供部分文本。fetch 语义抑制进度及中间 readyState；提供 `redirect`（包括 `"follow"`）或设置 `anonymous: true` 也选择该语义。详见[响应契约](usage-guide.zh-CN.md#响应与回调)及[网络证据](compatibility/network.md)。

响应流的 Promise 在响应头和流可用时兑现；后续失败通过流传播，`onload`/`onloadend` 随网络完成触发。renderer 拉取和宿主暂存限制桥接队列及磁盘用量，但不能暂停 CEF 网络源。流暂存在 EOF、取消或执行结束时释放。

HTTP `execute` 和 `readBody` 豁免 `BridgeOptions.RequestTimeout`；其他会话操作使用普通超时。所有请求（包括 `execute`）均计入 `MaxPendingRequestsPerDocument`。HTTP 自身 timeout 独立生效，fetch 语义除外。

Blob/File 下载逐块确认暂存后进入保存、取消及原子提交流程。自定义下载服务须读取 `DownloadRequest.Body`，不能通过 HTTP 解引用 blob 标识，并在终态事件前保持正文可用。失败或导航释放未完成的暂存；已接受下载可在文档结束后继续，文档回调停止，宿主释放会取消任务。

### Cookie

默认 `CefSharpCookieService` 使用宿主共享 CDP 会话及已附加的 RequestContext，通过 `new CefSharpCookieService(host)` 创建。Core 检查授权、脚本 URL 范围和宿主许可；后端映射 SameSite、读回真实身份/属性并验证精确删除。`ICookieService` 仅实现 CRUD，不提供自有监听。脚本参数及返回约定见 [Cookie 指南](usage-guide.zh-CN.md#cookie)。

### 分区 Cookie 范围

默认后端在已测 121/151 支持可序列化 `partitionKey` CRUD；84 在副作用前拒绝显式分区键。ancestor 维度在 151 可用，121 明确拒绝。规范化使用对应 Chromium 版本的公共后缀快照，不支持 opaque/nonce 键。详见[分区输入及身份规则](usage-guide.zh-CN.md#分区-cookie-范围)。

自定义分区后端除 CRUD 外实现 `IPartitionedCookieService.NormalizePartitionKeyAsync`，在 Core 过滤前验证真实支持并规范化键。其他后端拒绝 `partitionKey`，不能静默访问普通存储。

分区 CRUD 不代表 XHR `cookiePartition` 可用：CEF 公开请求 API 无法选择所需的可信顶层分区，默认后端在发送前拒绝。详见 [Cookie 范围与证据](compatibility/cookies.md#partitioned-cookie-scope)。

### Web Request

存在 webRequest 服务（包括默认服务）时，`Attach` 要求 `RequestHandler` 槽位为空或使用 `CefSharpWebRequestHandlerMultiplexer`。应在初始化前附加以覆盖首批资源。允许晚附加，并通过 `IsLateAttach` 标记，但无法补拦截已发送的请求。已有应用处理器时，在附加前组合：

```csharp
var requestHandlers = new CefSharpWebRequestHandlerMultiplexer();
requestHandlers.Add(applicationRequestHandler);
browser.RequestHandler = requestHandlers;
host.Attach(browser);
```

适配器拒绝被非 multiplexer 处理器占用的槽位。禁用默认服务后，显式 `UseWebRequestService` 仍需遵循上述组合方式。渲染处理器另需 `RenderProcessMessageHandlerMultiplexer`，示例见 [README](../README.zh-CN.md#已有的浏览器处理器)。

静态 `@webRequest` 在安装期编译，通过 `host.NavigateAsync` 在导航前激活；仓库变更撤销旧规则。静态加载遵循 URL、frame、run-in 及宿主策略，不能弹出交互授权。动态规则属于一个 browser/frame/document/execution，导航、更新、禁用、卸载及 detach 撤销。无法确定可信归属的请求不继承其他 frame 的规则。

selector、action、替换/撤销及回调见[请求拦截指南](usage-guide.zh-CN.md#request-interception)。历史 MV2 一致性、OOPIF、isolated frame 动态页面拦截及 WebSocket 覆盖均有明确[证据限制](compatibility/README.md#historical-webrequest)。

## 私密浏览

声明 `ITabService.SupportsIncognito` 的宿主必须打开真实私密 RequestContext；不支持的服务在打开前拒绝私密请求。`CefSharpHostOptions.IsIncognito` 应从宿主实际拥有的上下文解析，使 `@run-in`、`GM.info.isIncognito`、网络服务及标签打开保持一致。Demo 省略 `incognito` 时继承来源标签，false 显式选择普通标签。

Demo 的 `TabContext` 绑定稳定标签身份、RequestContext、隐私状态、值存储、权限策略和标签数据服务。私密标签共享独立内存上下文、临时权限/标签数据及逐脚本复制的普通 GM 初值；私密写入和监听不回流到普通存储。最后私密标签关闭后，先排空其宿主任务，再释放上下文并清除会话。

profile 处理、保存文件/剪贴板例外及脚本行为见[私密浏览](usage-guide.zh-CN.md#private-browsing)和 [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md)。[兼容性证据](compatibility/README.md#private-contexts)记录已验证的上下文隔离。

## 标签音频

默认 `ITabAudioService` 在 84/121/151 提供原生静音读写及变更监听。在 CEF UI 线程访问原生状态，每 200 ms 轮询其他宿主操作，因此可能漏报更短的变化。不提供可靠 audible 来源，能力报告 `partial:mute-only;audible-unavailable`。已测 151 导航会重置原生静音，不能保证跨导航保留。

通过 `UseTabAudioService` 替换，或设 `EnableDefaultAudioService = false` 省略默认服务；网络服务选项不控制音频。应用传入的服务仍归应用所有。状态、回调及版本差异见[音频 API](usage-guide.zh-CN.md#tab-audio)及[原生证据](compatibility/README.md#tab-audio)。

## 生命周期与诊断

`PrepareAsync()` 准备当前附加会话的共享 CDP；`NavigateAsync(url)` 等待准备后导航。使用该顺序时，浏览器应以 `about:blank` 初始化。`IsLateAttach` 与 `IsLatePreparation` 分别标记附加、准备发生时初始化已完成。这些 API 不提高用户脚本的时序保证。参见[真实协议实验结论](compatibility/execution-worlds.md#protocol-session-and-preparation)。

适配器为每个框架文档建立身份，并对每个 `(document, frame, script, run-at)` 组合执行一次。`@run-at context-menu` 脚本会按当前 frame 匹配规则出现在 CefSharp 原生上下文菜单中，只有用户选择菜单项后才创建一次新的执行。导航、上下文释放和分离使文档能力失效并取消待处理调用；`ContextReleased` 和 `NavigationSuperseded` 都会结束该文档的执行。已接受下载继续作为宿主任务运行，文档回调停止。宿主释放时取消这些任务。

`GM.info` 是每次执行固定的信息快照，后续执行使用当前文档 URL 和环境事实。同文档 URL 变化不会重新注入脚本；SPA 更新请使用已授权的 [window.onurlchange API](usage-guide.zh-CN.md#window-extensions)。

CefSharp 只能提供尽力而为的 document-start 钩子。相应执行产生 `MSR100_DOCUMENT_START_BEST_EFFORT`；设置 `RequireGuaranteedDocumentStart = true` 后会跳过并产生 `MSR101_DOCUMENT_START_UNAVAILABLE`。

匹配前，适配器读取当前 renderer 的 `location.href`，因为旧版 CefSharp 的 context-created 回调可能仍携带上次导航 URL。只读求值取消后，在本次初始化的 15 秒总期限内重试；每次求值最多两秒，重试间隔为不超过剩余期限的 50 ms。调用方取消、分离、文档会话被替换或原生 frame 失效时停止初始化，不回退使用过期的回调 URL。耗尽求值期限会产生初始化诊断，其中包含 `TimeoutException` 和最后一次取消异常。读取失败时跳过该文档，包括 load fallback 及上下文菜单执行。document-start 仍为尽力执行。

协议错误使用稳定代码 `MSP001` 至 `MSP010` 以及 `MSP999`。诊断不会包含能力令牌或存储值。将新建的 `BridgeOptions(...)` 赋给 `CefSharpHostOptions.Bridge` 即可配置限制：

| 选项 | 默认值 | 适用范围 |
| --- | --- | --- |
| `MaxRequestBytes` | 1 MiB | 每条 UTF-8 桥接请求，包括 HTTP 正文分块消息。 |
| `MaxResponseBytes` | 1 MiB | 每条 UTF-8 桥接响应，包括完整的 hello 响应和正文读取结果。 |
| `MaxResourceBytes` | 10 MiB | 单个资源 API 内容；安装内容总量由 `UserScriptPreparer` 单独限制。 |
| `RequestTimeout` | 30 秒 | 普通桥接请求；HTTP `execute` 和 `readBody` 豁免。 |
| `MaxPendingRequestsPerDocument` | 64 | 同一文档的并发桥接请求，包括 HTTP `execute`。 |
| `MaxCompatibilityBootstrapBytes` | 64 MiB | 序列化快照总预算；存储和资源快照超过单个响应时按索引分块，在用户代码启动前完整重组。 |
| `CompatibilityBootstrapTimeout` | 5 秒 | 每份存储或资源快照的准备时间。 |
| `MaxReplayEntriesPerExecution` | 4096 | 每次执行保留用于拒绝重放的近期请求 ID 数量。 |
| `MaxBinaryTransferBytes` | 256 MiB | 每个请求体、响应体或暂存下载，独立于桥接消息大小。 |

快照准备失败会阻止用户代码启动，并报告 `MSC410_COMPATIBILITY_BOOTSTRAP_FAILED`；大小错误需要调整预算。存储重放历史丢失时，`MSC414_STORAGE_REPLAY_RESET` 恢复当前值，不伪造已丢失回调，详见[存储同步](#storage-snapshots-and-listeners)。

## 原生隔离世界与生命周期

适配器通过原生 CDP frame/loader/context 事件、安装/版本作用域、context-ID 绑定及远程全局对象 ID 派发隔离源码和通知。对象 ID 固定 context 代际，避免数字 ID 复用后将响应发送到错误上下文。页面绑定仅接受有效页面世界文档，拒绝隔离执行凭据；共享协议域及其他观察者保持可用。`DocumentFrame` 暴露真实 `ExecutionWorld` 和可选 `ScriptScope`、`RevisionScope`；frame ID 为不透明标识，不将 CEF ID 强制转换为 CDP ID。`UserScriptExecutionEnvironment` 提供服务、隐私及更新事实，执行世界信息来自原生 frame。

`@sandbox DOM`/`@inject-into content` 选择隔离 context，raw/默认/JavaScript 选择页面访问。DOM 与显式 `unsafeWindow` 授权、冲突世界或未支持的 unwrap 以 `MSR215_EXECUTION_WORLD_UNAVAILABLE` 拒绝，不退回 raw。隔离 OOPIF 通过原生 child session 支持已验证的源码/存储/资源/XHR 范围，包括普通/私密上下文及迁移，详见 [OOPIF 路由](compatibility/execution-worlds.md#isolated-oopif-routing)。

BFCache 缓存执行会被撤销，需要新导航；隔离世界不支持历史 page-frame webRequest。页面世界的手动菜单使用当前原生 CEF frame 会话。隔离世界的手动菜单在三个已测版本均支持主 frame，84 另支持同进程子 frame；121/151 仍仅主 frame，隔离 OOPIF 菜单不支持。[执行证据](compatibility/execution-worlds.md)及 [start/CSP 决策](compatibility/start-csp-decision.md)分别说明这些边界、严格时机与 CSP 效果。

## 桥协议

桥接使用协议 `2`。消息声明的协议版本不匹配时，返回 `MSP001_PROTOCOL_VERSION`。

## 构建、打包与验证

清理并验证源码基线可运行 `./scripts/compatibility/Run-Baseline.ps1 -Platform x64 -CefSharpVersion 84.4.10`（x64 包含 WPF）。完整原生/包消费门禁使用 `./scripts/compatibility/Run-Delivery.ps1`，隔离各版本/平台、核查包内容并运行公开 API 消费宿主。`-CorpusScript <path>` 可额外原样安装本地脚本，`-PackageOnly` 跳过基础门禁、仅供诊断。范围见[验证说明](compatibility/validation.md#reproduction)，参数见[工具说明](../scripts/compatibility/README.md)。

CI 在目标分支为 `dev` 或 `main` 的 PR 上运行，覆盖 CefSharp `84.4.10`、`121.3.70`、`151.3.240` 的 x64/x86，WPF 仅 x64。工具链为 Windows Server 2022、.NET SDK `9.0.315` 和 Node.js `24`，这些是已测试版本而非最低要求。SDK 须支持 `MonkeySharp.slnx`；Core 测试目标为 `net8.0`，CefSharp 测试/应用目标为 `net462`。真实 Chromium 门禁需要 Windows 桌面会话。

还原、构建、测试和打包必须使用相同的 `CefSharpVersion` 属性：

```powershell
dotnet restore MonkeySharp.slnx -p:Platform=x64 -p:CefSharpVersion=151.3.240
dotnet build MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-restore
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 --no-build
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
```

使用 `-p:Platform=x86` 重复 .NET 命令；x86 配置会排除仅支持 x64 的 Demo 项目。Core 及不同平台适配器应输出到不同目录：

Solution 的两个平台均将 Core 映射到 AnyCPU，产物位于 `bin/Release/<framework>/`。打包 Core 时应使用 `-p:Platform=AnyCPU`，通过 `--no-build` 复用 solution 产物时也应如此。CefSharp 适配器则需要显式指定 x64 或 x86 平台。

```powershell
dotnet pack Mzying2001.MonkeySharp.Core/Mzying2001.MonkeySharp.Core.csproj -c Release -p:Platform=AnyCPU -p:CefSharpVersion=151.3.240 -o artifacts/packages/core
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x64 -p:CefSharpVersion=151.3.240 -o artifacts/packages/x64
dotnet pack Mzying2001.MonkeySharp.CefSharp/Mzying2001.MonkeySharp.CefSharp.csproj -c Release -p:Platform=x86 -p:CefSharpVersion=151.3.240 -o artifacts/packages/x86
```

在 Windows 桌面会话中运行真实 Chromium 验证：

```powershell
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x64 -CefSharpVersion 151.3.240
./Mzying2001.MonkeySharp.CefSharp.SmokeHost/Run-E2E.ps1 -Platform x86 -CefSharpVersion 151.3.240
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -CefSharpVersion 151.3.240
```

验证全过程应使用同一版本值：`dotnet restore/build/test/pack` 使用 `-p:CefSharpVersion=<版本>`，PowerShell runner 使用 `-CefSharpVersion <版本>`。SmokeHost 应分别对 x64/x86 和 `84.4.10`、`121.3.70`、`151.3.240` 运行；Demo gate 覆盖三个版本的 x64，每个版本只运行一次默认英文界面。每份报告记录 `environment.uiLanguage`，并验证英文窗口及权限控件；其他语言由本地化单元测试覆盖，详见[界面语言和部署](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md#界面语言)。在已有工作区切换版本前，先执行 `dotnet clean MonkeySharp.slnx -c Release -p:Platform=x64 -p:CefSharpVersion=<目标版本>`（位数使用目标平台），避免混用不同 CefSharp 版本的增量构建产物。SmokeHost runner 已使用选定版本执行 restore、clean、build，并核对实际运行的程序集版本和进程位数。两个验证入口都使用隔离 profile 和 `TrustedPageWorld`；它们是集成测试，不是安全沙箱。

### Chromium 兼容性脚本包

SmokeHost 通过正常安装流程，在 loopback HTTP fixture 上运行仓库原创脚本。runner 要求全部 15 个兼容性场景，核验实际 CefSharp 版本、平台及清理，并将日志保存在 `artifacts/smoke/<version>-<platform>/`；可通过 `-OutputDirectory` 覆盖。

场景 ID、断言、超时、解析器检查及日志排查见 [SmokeHost 场景与结果验证](compatibility/validation.md#smokehost-cases-and-results)。[验证指南](compatibility/validation.md#latest-recorded-validation)维护已记录结果及剩余缺口，[工具说明](../scripts/compatibility/README.md)维护 runner 参数及固定参考环境前置条件。
