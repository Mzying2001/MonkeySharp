# MonkeySharp WPF 浏览器示例

[English](README.md) | **中文**

带有后台标签页、原生用户脚本管理器和 SQLite 脚本/GM 值存储的便携式 WPF 浏览器。

## 构建与运行

Demo 面向 .NET Framework 4.6.2，使用 **x64**。构建需要兼容的 .NET SDK；运行需要 Windows、兼容的 .NET Framework，以及 CefSharp 所需的 x64 Visual C++ 运行库。依赖版本定义在[项目文件](Mzying2001.MonkeySharp.Demo.csproj)中。

在仓库根目录运行：

```powershell
dotnet build Mzying2001.MonkeySharp.Demo/Mzying2001.MonkeySharp.Demo.csproj -c Release -p:Platform=x64
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe
```

部署时复制整个输出目录，包括完整的 `CefSharp/` 子目录、`MonkeySharpDemo.exe.config` 和 `zh-CN` 卫星资源目录。CefSharp 原生依赖、浏览器子进程和资源通过官方 `CefSharpTargetDir` 属性输出到 `CefSharp/`，托管程序集保留包的默认输出行为。请使用可写的应用目录，Demo 的便携配置保存在可执行文件旁的 `Data/` 中。

默认 `CefSharpVersion` 为 `156.0.150`。源码构建可通过 `-p:CefSharpVersion=<version>` 覆盖版本，但所有托管和原生组件必须使用同一版本。运行 `Run-E2E.ps1` 时传入 `-CefSharpVersion <version>`，以测试相应版本。切换版本前应清理输出，避免残留旧版原生 DLL。除非指定 `-NoBuild`，Runner 会执行还原、清理和构建，并保留配置目录。已测试版本见[验证](#验证)。

## 界面语言

在 **Tools → Language (restart required)** 中选择 **English** 或 **简体中文**，然后关闭并重新打开 Demo。中文界面对应 **工具 → 语言（重启后生效）**。

默认语言为英文。使用 `--lang en` 或 `--lang zh-CN` 可临时覆盖本次启动的语言，不修改已保存的设置：

```powershell
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe --lang zh-CN
& .\Mzying2001.MonkeySharp.Demo\bin\x64\Release\net462\MonkeySharpDemo.exe --lang en
```

Windows 文件对话框中的系统控件跟随操作系统语言。脚本元数据、源码，以及运行时或第三方诊断消息保留原文。

## 浏览器与脚本管理器

- 新建/关闭标签页：Ctrl+T / Ctrl+W。地址栏：Ctrl+L。刷新：F5。开发者工具：F12。
- 切换标签页时保留浏览器控件，后台标签页继续运行。关闭当前标签页时优先返回指定的父标签页，否则选择左侧相邻标签页；关闭第一个时选择下一个。关闭后台标签页不改变当前选择，关闭最后一个会打开空白标签页。
- 网页弹窗和新标签页链接会作为受管理的标签页打开，不保留弹窗尺寸和 JavaScript `window.opener` 关系。
- 打开 **Script manager（脚本管理器）**，新建脚本、导入 UTF-8 `.user.js` 文件，或从 HTTP(S) URL 加载草稿。审核源码后选择 **Install / Save（安装 / 保存）** 或按 Ctrl+S，确认权限并安装。存在未保存修改时，切换选择或关闭管理器会要求确认。
- AvalonEdit 源码编辑器支持行号、JavaScript 高亮、四空格 Tab / Shift+Tab 缩进、自动缩进，以及 Ctrl+Z / Ctrl+Y 撤销/重做。右键菜单提供撤销、重做、剪切、复制、粘贴、删除和全选，命令作用于源码编辑器。长行可横向滚动。模板字符串按字符串着色，不单独高亮插值表达式。
- 脚本列表支持按名称、描述、命名空间或来源搜索，按状态筛选，按名称排序，内联启用/停用开关和删除。无效元数据会阻止安装或替换旧版本。
- 元数据详情只读，按基本信息、匹配规则、权限、依赖/资源和诊断分组；修改元数据应编辑源码。图标优先使用 `@icon64`，其次为 `@icon`；WPF 加载 HTTP(S) 图标，不可用时使用名称首字母占位。
- **Check and install updates（检查并安装更新）** 优先使用 `@updateURL`，其次为 `@downloadURL`；`@downloadURL none` 关闭检查。更新必须具有相同且非空的名称和命名空间，以及更新的版本。检查使用独立 HTTP 客户端，不携带浏览器登录 Cookie，没有后台更新任务。
- 安装、编辑、启用、停用或删除脚本会刷新**所有已打开的标签页**，包括 frame，以撤销旧执行状态并重新运行匹配脚本。
- 注册命令显示在 **Script menu（脚本菜单）** 下，支持 Alt+访问键。匹配的 `@run-at context-menu` 入口显示在原生页面右键菜单中，选中后创建新的执行。**Tools（工具）** 提供诊断、下载取消控件、开发者工具、数据/下载目录和语言设置。

## 便携数据与恢复

配置保存在可执行文件旁，不受进程工作目录影响：

```text
<可执行文件目录>/Data/
  .profile.lock
  ui-language.txt
  monkeysharp.db
  BrowserCache/Default/
  UserScripts/<script-key>.user.js
  Dependencies/<cache-key>.json
  Downloads/
  Logs/demo.log
```

`ui-language.txt` 保存语言选择，设置缺失、不可读或不受支持时回退为英文。SQLite 保存脚本安装记录、类型化 GM 值、已记忆的权限和活动标签页状态。源码以 UTF-8 文件保存，按稳定脚本身份命名。尚未实现浏览器会话恢复，因此启动时会清理旧标签页状态。

目录不可写，或另一个进程正在使用该配置目录时，启动会失败。请使用可写的应用目录，无需提升进程权限；程序不会回退到 AppData。应在**关闭应用后**备份整个 `Data/`，包括存在的 SQLite `-wal`/`-shm` 文件。

数据库使用格式 **2**。不受支持的格式会中止启动，并保留已有数据库。如需使用空数据库启动，先备份配置目录，再将 `Data/monkeysharp.db` 及其 `-wal`/`-shm` 文件移出目录后重新启动。这不会恢复脚本或 GM 值，也不会自动重新导入源码文件。

源码修改通过暂存文件、数据库事务和已提交哈希进行恢复。源码缺失、无效或哈希不匹配的脚本仍然可见，但会被停用；恢复失败记录在诊断中。不要在应用运行时从外部编辑已安装源码，应将审核后的源码导入或粘贴到管理器的已有条目，以保留脚本身份和 GM 值。删除脚本也会删除其存储值。

诊断写入 `Logs/demo.log`，达到约 5 MiB 时轮转并保留一份上一版本日志。Demo 禁用了原生 CEF 文件日志。

## 私密浏览

通过 **Private +（私密＋）** 或 `GM.openInTab(url, {incognito:true})` 打开私密标签页。省略 `incognito` 时继承来源标签页的隐私状态，`false` 显式打开普通标签页。宿主通过 `ITabService.SupportsIncognito` 声明支持情况，不支持时会在打开前拒绝私密请求。

私密标签页共享独立的内存 RequestContext，与普通 Cookie 和缓存隔离。权限决定和标签页数据也保存在内存中。关闭最后一个私密标签页时，先取消并等待其宿主操作结束，再释放上下文并清空会话。`@run-in`、`GM.info.isIncognito`、网络 API 和标签页打开均使用标签页的实际上下文。

遵循固定 Tampermonkey 5.5.1 的临时会话行为，每个脚本首次私密访问时复制普通 GM 值基线。此后的私密写入和监听仅限于私密会话，不回流到普通存储，并在最后一个私密标签页关闭后清除。标签页状态枚举也按隐私上下文隔离。

私密控制台/运行时诊断和标签页数据不会写入普通配置目录。Demo 禁用了 CEF 之外的私密 favicon 加载，私密下载历史会在所属标签页关闭时移除。显式保存的下载文件和复制的剪贴板内容仍属于用户输出。详见[私密上下文行为与证据](../docs/compatibility/README.md#private-contexts)。

## API 集成边界

| API 分组 | Demo 实现 |
| --- | --- |
| 值与值监听 | 普通标签页共享 SQLite 存储，支持类型化值、插入顺序、串行化写入、原子快照、提交后通知和旧式存储镜像。私密会话将普通基线复制到隔离内存中。 |
| DOM、info、log | 使用 Core 门面，日志同时显示在诊断面板中。 |
| `@require`、`@resource` | 按安装/修订版本持久化 URL 缓存，缓存前和读取时均按原始字节检查 SRI `md5`/`sha256`。支持 HTTP(S)，最多 10 次重定向，超时 30 秒，每项上限 10 MiB。 |
| 分区 Cookie | 已测试的 121/151 支持 CDP CRUD，84 拒绝。XHR 分区选择在发送前被拒绝。 |
| XHR、Cookie、webRequest | CefSharp 服务使用浏览器的实际 RequestContext。自定义导航处理器使用适配器的请求处理器多路复用器。 |
| 音频 | 读取、修改和监听原生静音状态，不提供 audible 状态。 |
| 菜单 | 每标签页独立命令、回调和访问键，执行结束时清理。 |
| 通知 | 非模态 WPF 窗口，支持完成 Promise、tag 替换、可取消的 URL 点击、标签页高亮、静音、超时和有大小限制的 HTTP(S) 图片，无需注册系统 toast。 |
| 剪贴板 | 通过 Dispatcher/STA 写入 Unicode 文本和 CF_HTML，争用时进行有限重试。 |
| `openInTab` | 遵循 active/insert/setParent/incognito，观察用户关闭并支持显式关闭。释放脚本句柄不会关闭已打开的标签页。 |
| 下载 | HTTP(S) 流式下载、进度、请求头、冲突处理（`uniquify`、`overwrite`、`prompt`）、取消、超时和失败回调。默认保存到 `Downloads`，文件名会净化，部分下载文件会被清理。 |
| 标签页状态 | 以脚本和活动标签页身份为范围保存 JSON 对象，跨导航保留，标签页关闭时移除。 |

源码/资源下载和更新检查使用独立 HTTP 客户端，不携带浏览器凭据。GM 下载和 GM XHR 共用 CEF HTTP 服务与 RequestContext，包括登录 Cookie、匿名请求和重定向授权。

`GM.download` 立即返回可中止的完成 Promise，`GM_download` 返回仅支持中止的句柄；完成发生在文件清理或原子提交之后。已接受的下载在导航、脚本停用或卸载后仍按已有授权继续执行，但不再向已结束的执行发送回调；重定向目标仍需授权。关闭标签页或释放宿主时，会取消并等待任务结束，再释放浏览器上下文。详见[下载行为](../docs/compatibility/downloads.md)。

脚本可通过元数据选择适配器的 DOM/content 隔离世界；Demo 同时为页面世界脚本启用 `TrustedPageWorld`。这些模式仍遵守适配器的[执行与 frame 限制](../docs/compatibility/execution-worlds.md#remaining-limits)。完整 API 参数及宿主契约见[用户脚本指南](../docs/usage-guide.zh-CN.md)和[集成参考](../docs/integration-reference.zh-CN.md)。

Demo 不提供后台自动更新、云同步、书签、完整历史记录、脚本签名或扩展商店安装。

## 安全

**Demo 启用 TrustedPageWorld，请只运行可信脚本并访问可信页面。** `@grant`、`@connect`、权限检查和桥接令牌仍然适用，但页面世界中的代码可能观察或使用已暴露的能力。安装确认是审核步骤，不构成安全沙箱。

Demo 使用 `InteractiveUserScriptPermissionPolicy` 和 `SensitiveUserScriptPermissionsPolicy`。`GM.xmlHttpRequest`、`GM.cookie` 和 `GM.download` 请求按目标范围提示授权，已记忆决定的范围除外。可选择 **Allow once（允许一次）**、**Remember allow（记忆允许）** 或 **Deny and remember（拒绝并记住）**。打开 **Permissions（权限）** 可查看或撤销某个脚本的记忆决定，撤销后会刷新标签页。用户同意不能扩大声明的授权或目标规则。

生产宿主应根据自身威胁模型提供信任策略和实际隔离。Demo 不会禁用证书校验或 Chromium Web 安全机制。

## 代码组织

| 位置 | 职责 |
| --- | --- |
| `Runtime/` | 配置目录初始化、宿主组装、标签页上下文、共享私密会话和关闭流程。`TabRuntime` 管理单个浏览器、宿主和下载服务。 |
| `Views/`、`Browser/` | WPF 窗口、浏览器绑定，以及 CefSharp 导航、弹窗、显示、下载和 favicon 处理器。 |
| `ViewModels/` | 标签页选择、草稿、脚本管理器操作、诊断和显示状态。 |
| `Services/`、`Persistence/` | WPF 宿主服务、HTTP 源码加载、SQLite 事务、类型化存储和源码恢复。 |
| `Localization/`、`Properties/` | 应用语言选择和资源访问辅助代码，以及中英文 RESX 资源。 |
| `Automation/`、`Run-E2E.ps1` | Loopback fixture、浏览器断言、轮询、隔离配置目录和 JSON 报告。 |

MonkeySharp 宿主必须在浏览器加入可视树之前附加。标签页释放时依次停止浏览器、取消下载、分离并释放宿主、等待已接受任务结束，最后释放浏览器和上下文。重复释放或关闭会等待同一个操作。自定义持久化实现应遵守[宿主服务契约](../docs/integration-reference.zh-CN.md#宿主服务契约)。

`Properties/Resources.resx` 保存英文资源，`Properties/Resources.zh-CN.resx` 保存中文资源。XAML 使用 `{loc:Text Key}`，C# 使用 `Strings.Get` / `Strings.Format`。每种译文应保留完整消息及一致的占位符。新增语言需要资源文件、受支持语言条目、菜单项和本地化单元测试，E2E 始终只运行英文。自动化 ID 独立于可见标签。

## 验证

在仓库根目录运行，真实 Chromium/WPF E2E 需要 Windows 桌面会话：

```powershell
dotnet test MonkeySharp.slnx -c Release -p:Platform=x64
node --test Mzying2001.MonkeySharp.Core.Tests/JavaScript/*.test.js
powershell -NoProfile -ExecutionPolicy Bypass -File Mzying2001.MonkeySharp.Demo/Run-E2E.ps1
# 复用已匹配版本的构建：
./Mzying2001.MonkeySharp.Demo/Run-E2E.ps1 -NoBuild
```

Runner 使用默认英文界面和隔离的 `SmokeRuns/<id>/Data` 配置目录启动一个进程，检查界面标签、权限、标签页与 frame、值和 Cookie、资源、XHR/webRequest、菜单、通知、真实下载、私密上下文、脚本变更和最终释放。剪贴板检查会临时写入 Unicode 文本和 HTML，然后恢复原内容。测试项目禁用 shadow copy，以便从测试输出中解析 SQLite 原生库。

JSON 报告写入 `artifacts/demo-smoke`，`environment.uiLanguage` 记录界面语言，`profile` 指向包含日志和截图的运行目录。其他语言由本地化单元测试检查。CI 对每个受支持的 x64 CefSharp 版本执行一次英文 Demo E2E：`84.4.10` 和 `156.0.150`；SmokeHost 还覆盖 x86。完整矩阵见[验证指南](../docs/compatibility/validation.md)。

手动检查项：切换语言并重启；用 `--lang` 临时覆盖启动语言；安装/编辑/删除脚本；拒绝权限请求；存在未保存修改时关闭；确认/取消另存为；重启后验证登录持久化；检查源码缺失或被锁定时的恢复；使用同一配置目录启动第二个进程，确认启动错误。
