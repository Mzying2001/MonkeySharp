# MonkeySharp 更新日志

[English](changelog.md) | **中文**

## 4.1.0（未发布）

- 新增 `CefSharpUserScriptHost.TryGetContextMenuScriptKey`。自定义 CefSharp 菜单处理程序可用 `@run-at context-menu` 菜单项的命令 ID 查询安装键，并根据安装键为同名脚本选取正确的图标。命令 ID 仅在当前菜单中有效。 [集成参考](integration-reference.zh-CN.md#交互服务) · [Git 提交](https://github.com/Mzying2001/MonkeySharp/commit/2514967ec798caaa51e1e475bcd455881165a7af)

## 4.0.0 — 2026-11-17

### 破坏性变更

- 使用由 `IUserScriptPersistence` 提供存储的 `UserScriptRepository`，替代 `InMemoryUserScriptRepository` 和 `PersistentUserScriptRepository`。使用仓库或附加宿主前，须调用 `InitializeAsync`。自定义持久化实现改用类型化的安装记录和资产记录，并在条件写入时校验源码版本。 [持久化契约](integration-reference.zh-CN.md#持久化安装)
- 资源 API 使用声明的字符集或 UTF-8 从原始字节解码文本。宿主资源提供程序需提供字节和编码元数据。依赖脚本使用严格解码，资源文本中的无效字节序列以替代字符处理。 [资源准备](integration-reference.zh-CN.md#资源完整性与更新检查)
- Demo 使用 SQLite 数据库（架构版本 3）保存安装元数据，以 UTF-8 文件保存源码，并在按内容寻址的缓存中共享依赖与资源。从 3.x 升级时，需先备份 Demo 数据目录，再重建数据库并手动重新安装脚本。 [Demo 数据与恢复](../Mzying2001.MonkeySharp.Demo/README.zh-CN.md#便携数据与恢复)

### 新增与改进

- 新增独立的源码重载和依赖与资源刷新。编辑器采用条件保存，遇到源码或修订冲突时保留草稿。Demo 会显示已启用但不可用的安装。
- 区分启用意图与准备状态。无法完成准备的安装会停止执行，过期的执行会话和静态请求规则会被撤销。重载后的脚本在后续导航时使用新修订。
- Demo 可恢复中断的源码写入并回收未引用的资产。仍被其他安装引用的资产会保留。

[与 3.0.1 相比的 Git 变更](https://github.com/Mzying2001/MonkeySharp/compare/3.0.1...4.0.0)

## 3.0.1 — 2026-11-14

- 修复 HTTP 请求和下载在重定向时切换 CefSharp 请求上下文的问题。下载保留发起文档以匹配作用域内的 `GM.webRequest` 规则，脚本安装发生变更后，已受理的下载仍可继续，重定向须通过授权检查。
- 修复脚本安装变更后过期执行及其监听器未清理的问题。隔离世界的桥调用统一使用 Core 的超时机制，避免在权限提示或读取响应体时触发额外超时。
- 修复同一文档内 URL 变化后隔离世界菜单回调失效的问题，并恢复 Demo 从页面 DOM 查询网站图标的功能。
- 修复 `@antifeature` 重复声明同一类型和语言时脚本注入失败的问题。解析时保留第一条声明，并对重复项发出警告。
- 将包版本集中到 `Version.props`，构建脚本从项目配置读取默认 CefSharp 版本。发布 NuGet 包后自动创建 GitHub Release，并附上包与符号包。

[与 3.0.0 相比的 Git 变更](https://github.com/Mzying2001/MonkeySharp/compare/3.0.0...3.0.1)

## 3.0.0 — 2026-11-14

- 将浏览器无关的运行时独立为 Core，由 CefSharp 宿主管理文档和帧的生命周期。新增经过身份校验的异步桥调用、授权检查、每脚本 DOM 隔离世界，以及对受支持的跨进程 iframe 的隔离执行支持。
- 扩展 Tampermonkey 兼容性，支持现代 `GM.*` 与旧式 `GM_*` API、旧式同步值与资源读取、批量存储与变更监听，以及受授权控制的窗口辅助方法。新增原生 `@run-at context-menu` 执行。
- 新增脚本持久化安装和更新检查，在激活脚本修订前准备 `@require` 和 `@resource`，并校验声明的完整性哈希。
- 提供基于 CefSharp 的 HTTP 请求和 Cookie 支持，以及带作用域的 `GM.webRequest` 取消和重定向规则。新增二进制与流式上传、增量文本响应、可中止的下载、标签页音频控制，以及受引擎版本支持的分区 Cookie 操作。
- 新增 WPF Demo，提供多标签浏览、SQLite 持久化、带语法高亮编辑器的脚本管理器、中英文界面，以及按脚本分组的菜单命令。Demo 支持私密浏览、交互式权限确认和记住权限决定。
- 发布共享的 Core NuGet 包，以及分别面向 CefSharp `156.0.150` 和 `84.4.10` 的 x64、x86 适配器包。每个适配器包声明精确的 CefSharp 依赖版本。

[3.0.0 的 Git 历史](https://github.com/Mzying2001/MonkeySharp/commits/3.0.0/)
