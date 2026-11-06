# MonkeySharp 用户脚本使用指南

[English](usage-guide.md) | **简体中文**

本文面向脚本作者，描述当前 MonkeySharp 3 源码中的 JavaScript 用户脚本功能，包括如何编写脚本、全部内置脚本 API，以及与 Tampermonkey 的行为差异。宿主应用可以调整服务、权限、限制和兼容模式。.NET 接入请参阅[集成参考](integration-reference.zh-CN.md)，安装和界面操作请参阅 [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.md)。

与 Tampermonkey 的比较于 **2026-11-01** 对照其[官方文档](https://www.tampermonkey.net/documentation.php)核对。比较的是公开行为约定，并不表示已对所有 Tampermonkey 和浏览器版本进行差分测试。

## 目录

- [编写并安装脚本](#write-and-install-a-script)
- [元数据](#metadata)
- [执行、授权与兼容模式](#execution-grants-and-compatibility)
- [API 索引](#api-index)
- [信息与日志](#information-and-logging)
- [存储与值变更监听](#values-and-change-listeners)
- [DOM 辅助方法](#dom-helpers)
- [资源](#resources)
- [HTTP 请求](#http-requests)
- [菜单、通知与剪贴板](#menus-notifications-and-clipboard)
- [标签页及标签页状态](#tabs-and-tab-state)
- [下载](#downloads)
- [Cookie](#cookies)
- [请求拦截](#request-interception)
- [window 扩展](#window-extensions)
- [Tampermonkey 兼容性检查表](#tampermonkey-compatibility-checklist)
- [错误与排查](#errors-and-troubleshooting)
- [参考来源](#reference-sources)

<a id="write-and-install-a-script"></a>

## 编写并安装脚本

### 第一个脚本：修改页面

将以下完整代码保存为 UTF-8 文件 `page-marker.user.js`：

```javascript
// ==UserScript==
// @name         Page marker
// @namespace    https://example.com/my-scripts
// @version      1.0.0
// @description  Show that a userscript ran
// @match        https://example.com/*
// @run-at       document-end
// @grant        none
// ==/UserScript==

(function () {
    const marker = document.createElement("p");
    marker.textContent = "MonkeySharp is running.";
    document.body.prepend(marker);
})();
```

在 WPF Demo 中打开脚本管理器，新建脚本或导入该文件，检查源码后选择**安装 / 保存**。确认安装并启用脚本，访问或刷新 `https://example.com/`，页面上应出现提示文字。Demo 安装或编辑脚本后会刷新所有已打开的标签页。其他应用可以使用自己的安装界面；仅打开 `.user.js` 链接不一定会触发安装。

`@match` 选择页面，`@run-at` 选择执行阶段；普通页面 DOM 操作使用 `@grant none` 即可。它不会授予存储、网络或宿主交互 API。

### 第二个脚本：持久化访问次数

以下完整脚本需要宿主允许特权授权。Demo 已显式启用所需的 `TrustedPageWorld` 选项。

```javascript
// ==UserScript==
// @name         Visit counter
// @namespace    https://example.com/my-scripts
// @version      1.0.0
// @match        https://example.com/*
// @noframes
// @run-at       document-end
// @grant        GM.getValue
// @grant        GM.setValue
// @grant        GM.addStyle
// ==/UserScript==

(async function () {
    const count = await GM.getValue("visits", 0) + 1;
    await GM.setValue("visits", count);
    await GM.addStyle("#ms-counter { padding: 8px; background: #fff3c4; }");
    const badge = document.createElement("p");
    badge.id = "ms-counter";
    badge.textContent = `Visits: ${count}`;
    document.body.prepend(badge);
})().catch(error => console.error("Visit counter failed", error));
```

刷新页面即可增加次数。存储属于脚本安装实例，而非网站。能否在应用重启后保留取决于宿主的存储实现；Demo 使用 SQLite。多个标签页同时运行时，先读后写并不是原子自增。

使用 async 函数并捕获其拒绝。脚本运行在普通函数包装中，而非 ES 模块，因此不能直接使用顶层 `await`。后续示例是代码片段：将其中列出的 `@grant` 加入脚本元数据，并在 async 函数内使用 `await`。

在 Demo 中按 F12 打开开发者工具。`GM` 和 `GM_*` 是包装函数的局部绑定，不一定存在于页面控制台或 `window.GM`。请在脚本中记录日志，或在执行位置设置断点。

<a id="metadata"></a>

## 元数据

元数据头以 `// ==UserScript==` 开始，以 `// ==/UserScript==` 结束。为便于迁移，请放在文件开头。默认解析器允许在前 128 行、64 Ki 个 UTF-16 字符内查找元数据头；元数据错误会阻止安装，警告则保留给宿主查看。已知单值字段使用第一次出现的值，重复声明产生警告；集合字段使用多行重复声明。未知字段会保留，但不会因此获得运行时效果。

| 字段 | 格式与行为 |
| --- | --- |
| `@name`、`@namespace`、`@version` | 脚本标识与版本。建议名称和命名空间非空且保持稳定，更新功能尤其需要如此。安装身份及存储另由稳定的 `ScriptKey` 标识。 |
| `@description`、`@author`、`@license`、`@copyright` | 描述性字符串。`@name:locale` 和 `@description:locale` 保留本地化版本。 |
| `@icon`、`@iconURL`、`@icon64`、`@icon64URL` | 图标 URL；带 URL 后缀的字段优先于对应短名称，Demo 优先显示 64 像素图标。 |
| `@match pattern` | 可重复的 WebExtension 风格正向匹配规则。至少需要一个匹配的 `@match` 或 `@include` 才能执行。 |
| `@include pattern` | 可重复的 URL 通配符或斜杠包围的 .NET 正则表达式，例如 `/^https:\/\/example\.com\//`。正则执行时间限制为 100 毫秒。 |
| `@exclude`、`@exclude-match` | 分别使用通配符/正则和 match 模式进行排除。任何排除规则都优先于正向规则。非法 match 模式和正则是元数据错误。 |
| `@run-at value` | `document-start`、`document-body`、`document-end`、`document-idle`（默认）或 `context-menu`，见后文执行说明。 |
| `@noframes` | 只要存在就禁止子框架执行，无需值；否则每个匹配的框架都可能执行。 |
| `@grant name` | 除默认可用的 info 属性外，为每项能力分别声明。规范名称及已知旧版别名区分大小写。`none` 不能与其他授权混用；未知授权产生警告且不会暴露。 |
| `@connect target` | XHR 目标白名单：主机名、IP、`localhost`、`*.example.com`、`self`、`*` 或绝对 HTTP(S) URL，详见 [HTTP 请求](#http-requests)。 |
| `@require url` | 通过宿主依赖提供器获取，在主源码前按声明顺序执行的依赖。它不是 API 授权。 |
| `@resource name url` | 命名资源，通过资源 API 和宿主资源提供器读取。名称区分大小写。 |
| `@homepage`、`@homepageURL`、`@website`、`@source`、`@supportURL` | 项目及支持链接。主页优先级为 homepageURL、homepage、website、source。 |
| `@downloadURL`、`@updateURL` | 更新信息。Demo 提供手动检查并安装，运行时不安排自动更新。`@downloadURL none` 禁止检查。 |
| `@antifeature type description` | 功能披露信息，支持 `@antifeature:locale` 本地化声明。 |
| `@webRequest JSON` | 校验后的 selector/action 元数据，可通过 `GM.info.script.webRequest` 查看，**不会**激活拦截；请使用 `GM.webRequest` 注册。 |
| `@run-in`、`@inject-into`、`@sandbox`、`@unwrap` | 保留执行声明，但不会创建扩展隔离世界、选择隐私/容器环境或移除包装函数。非 raw 沙箱和 unwrap 会产生诊断。 |

### 匹配与生命周期

`@match https://example.com/*` 匹配该主机；`@match https://*.example.com/*` 匹配裸域名 `example.com` 及其所有层级的子域名，与 [Tampermonkey 的 match 约定](https://www.tampermonkey.net/documentation.php?q=include)一致。`@exclude-match` 使用相同的主机通配符规则。`*://` 和 `http*://` 匹配 HTTP/HTTPS；显式端口会被检查。主机名会进行 IDN 规范化，URL 片段被忽略，查询字符串参与匹配。

MonkeySharp 还接受 `file:///*`、`about:blank` 和 `<all_urls>`（HTTP、HTTPS、file，不包含 about:blank），实际取决于应用提供的页面。Tampermonkey 的公开 match 语法不支持 `<all_urls>`。include/exclude 通配符针对规范化后的整个 URL 区分大小写匹配，支持 `*` 和 `?`，没有实现 Tampermonkey 基于主机的 include 特殊处理；选择主机时优先使用 @match。元数据正则使用 .NET 语法及 `/expression/` 分隔形式，不接受 `/i` 等 JavaScript 字面量标志。

| 执行阶段 | MonkeySharp 行为 |
| --- | --- |
| `document-start` | 由 CefSharp 尽力注入，不保证早于页面脚本。要求保证时机的宿主可以跳过此类执行。 |
| `document-body` | body 可用时执行。 |
| `document-end` | 对应 DOMContentLoaded 生命周期事件。 |
| `document-idle` | 对应 load 生命周期事件，不应假定与 Tampermonkey 的调度策略相同。 |
| `context-menu` | 仅在用户选择匹配脚本的原生右键菜单项后执行，每次选择都创建新的执行实例。 |

每个 `(document, frame, script, run-at)` 组合只执行一次。同文档导航会更新匹配上下文，并可产生 urlchange，但不会自动重跑同一生命周期调用。SPA 更新请使用 [window.onurlchange](#window-extensions) 或 DOM 观察器。完整导航会撤销旧执行实例及其待处理操作、监听器和注册项。

### 依赖、资源与更新

`@require` 和 `@resource` URL 支持 md5/sha256 完整性片段：`#sha256=<digest>` 或 `#sha256-<digest>`，摘要可以是十六进制或标准 Base64。以逗号/分号分隔的多个声明选取最后一个受支持的哈希。请求和缓存前移除 URL 片段；使用前及读取缓存时都检查原始字节。声明格式错误、没有可用受支持哈希或摘要不一致都会阻止加载。

获取及缓存由宿主负责。缺少依赖提供器时会跳过声明 require 的脚本，不区分调用风格。不要依赖未声明的全局变量，也不要假定内置 CDN 库。资源 API 还需要各自的授权和资源提供器。

Demo 手动更新时优先从 `@updateURL` 检查，其次使用 `@downloadURL`；安装源码优先取自 `@downloadURL`，其次使用检查来源。候选脚本必须具有相同且非空的名称/命名空间，下载版本必须更高。更新保留安装身份、启用状态及存储值。该下载器不带浏览器登录 Cookie，详见[更新与完整性参考](integration-reference.zh-CN.md#资源完整性与更新检查)。

<a id="execution-grants-and-compatibility"></a>

## 执行、授权与兼容模式

现在只有一套契约。[固定 Tampermonkey 5.5.1 证据](compatibility/api-contract.md) 确认：
未声明、none、未知授权和 info-only 均提供 GM_info 与 GM.info，但保留不同元数据状态。
none 不提供 unsafeWindow；其他情况指向页面 global。

旧式 GM_* 名称需要精确声明。现代存储、资源、菜单和标签数据 API 按家族开放：
GM_getValue 还允许现代存储写入，但不会暴露未声明的 GM_setValue 全局，也不授权其他家族。
宿主服务和权限政策仍决定操作是否可用、是否获准。

调用风格不强制 strict，也不选择世界；strict 由源码指令决定。顶层 this 为 window 门面，
none 按 5.5.1 实测使用独立对象 receiver。生产执行仍在可信页面世界，CDP 隔离世界实验
尚不是生产特权沙箱。document-start 保持 best effort，显式要求保证时拒绝执行。

旧式同步存储和资源依赖完整快照，默认预算 1 MiB、准备超时 5 秒。宿主交互需要对应服务。
[集成说明](integration-reference.zh-CN.md#授权与-api-契约) 说明信任、服务和真实环境信息。

<a id="api-index"></a>

## API 索引

除非另有说明，每行需要第一列中的能力授权。`Promise<T>` 表示使用 await；立即取得 ID/句柄并不意味着后台注册已完成。旧版名称需要精确声明。后文逐项说明详细行为、错误和示例。

| 能力 / 现代成员 | 旧版名称 | 现代返回值 | 实现来源 |
| --- | --- | --- | --- |
| `GM.info` | `GM_info` | 冻结对象，不是函数 | Core |
| `GM.log` | `GM_log` | `Promise<undefined>` | Core 日志回调 |
| `GM.getValue` | `GM_getValue` | `Promise<value>` | 值存储 |
| `GM.setValue` | `GM_setValue` | `Promise<undefined>` | 值存储 |
| `GM.deleteValue` | `GM_deleteValue` | `Promise<undefined>` | 值存储 |
| `GM.getValues` | `GM_getValues` | `Promise<object>` | 值存储 |
| `GM.setValues` | `GM_setValues` | `Promise<undefined>` | 值存储 |
| `GM.deleteValues` | `GM_deleteValues` | `Promise<undefined>` | 值存储 |
| `GM.listValues` | `GM_listValues` | `Promise<string[]>` | 值存储 |
| `GM.addValueChangeListener` | `GM_addValueChangeListener` | `Promise<number>` | Core |
| `GM.removeValueChangeListener` | `GM_removeValueChangeListener` | `Promise<undefined>` | Core |
| `GM.addStyle` | `GM_addStyle` | `Promise<HTMLStyleElement>` | 页面 DOM |
| `GM.addElement` | `GM_addElement` | `Promise<Element>` | 页面 DOM |
| `GM.getResourceText` | `GM_getResourceText` | `Promise<string>` | 资源提供器 |
| `GM.getResourceUrl` | `GM_getResourceURL` | `Promise<string>` | 资源提供器 |
| `GM.xmlHttpRequest` | `GM_xmlhttpRequest` | 可 abort 的 `Promise<Response>` | CefSharp 默认服务 |
| `GM.registerMenuCommand` | `GM_registerMenuCommand` | `Promise<ID>` | 菜单服务 |
| `GM.unregisterMenuCommand` | `GM_unregisterMenuCommand` | `Promise<undefined>` | 菜单服务 |
| `GM.notification` | `GM_notification` | `Promise<boolean>` | 通知服务 |
| `GM.setClipboard` | `GM_setClipboard` | `Promise<undefined>` | 剪贴板服务 |
| `GM.openInTab` | `GM_openInTab` | 立即返回标签页句柄 | 标签页服务 |
| `GM.getTab` | `GM_getTab` | `Promise<object>` | 标签页状态服务 |
| `GM.saveTab` | `GM_saveTab` | `Promise<undefined>` | 标签页状态服务 |
| `GM.getTabs` | `GM_getTabs` | `Promise<object>` | 标签页状态服务 |
| `GM.download` | `GM_download` | `Promise<DownloadDetails> & {abort()}` | 下载服务 |
| `GM.cookie` | `GM_cookie` | 含 list/set/delete 的命名空间 | CefSharp 默认服务 |
| `GM.webRequest` | `GM_webRequest` | 立即返回可移除句柄 | CefSharp 默认服务 |
| `unsafeWindow` | 同名 | 页面全局对象 | 精确授权 |
| `window.close`、`window.focus` | 同名 | 各返回 `Promise<boolean>` | 窗口服务 |
| `window.onurlchange` | 同名 | 属性/事件注册 | Core |

<a id="information-and-logging"></a>

## 信息与日志

### GM.info / GM_info

只读、递归冻结的安装信息快照，`GM_info === GM.info`。单一契约下两种名称都默认可用，无需授权。无授权、@grant none 或仅声明 info 授权的脚本无需桥接绑定和握手。元数据保留原始声明；没有参数，不返回 Promise。

| 字段 | 含义 |
| --- | --- |
| `scriptKey` | 稳定的安装标识。 |
| `name`、`namespace`、`description` | 顶层元数据便利字段。version 是管理器实际版本，脚本版本位于 script.version。 |
| `grants`、`grantDeclarationState` | 原始授权名称，以及 Missing、ExplicitNone 或 ExplicitList。授权别名在内部规范化，这些列表保留原始写法。 |
| `scriptHandler`、`sandboxMode` | 固定为 "MonkeySharp" 和 "raw"。 |
| `downloadMode`、`isIncognito`、`scriptWillUpdate` | 下载服务实际模式及宿主 context/更新策略；未知字段省略。 |
| `scriptMetaStr`、`scriptUpdateURL` | 原始元数据头和声明的更新 URL。 |
| `compatibility` | contractVersion: 2，包含实际世界、能力状态及字段来源。 |
| `script` | 下述脚本元数据对象。 |

script 包含 name、namespace、version、description、author、copyright、name_i18n、description_i18n、icon、icon64、homepage、supportURL、downloadURL、updateURL、fileURL（安装来源）、header、grant、connects、matches、includes、excludes（含 exclude-match）、resources、antifeatures、webRequest、run-at 和 unwrap。lastModified 是安装更新时间的 Unix **毫秒数**；不提供没有管理器状态来源的 position/blockers 字段。resources 项为 `{name, url, integrity: [{algorithm, digest}]}`；antifeatures 先按类型、再按语言分组（无语言时为 default）。未声明 webRequest 时为 null。options 包含 sandbox、noframes、unwrap 和 run_at。

**与 Tampermonkey 的差异：** 这是 MonkeySharp 自身的快照，包含来源标记和真实环境事实，不保证提供全部 [Tampermonkey GM_info 字段](https://www.tampermonkey.net/documentation.php?q=GM_info)、实时浏览器设置或扩展更新状态。

### GM.log(value) / GM_log(value)

value 使用 JSON.stringify/JSON.parse 克隆：顶层 undefined、函数、symbol、BigInt 和循环引用无效。日志规则与下方的存储值编码相互独立。现代方法在投递给宿主日志回调后返回 `Promise<undefined>`；未配置日志接收器时可能没有可见输出。GM_log 返回 undefined，同时输出到页面控制台，并将异步宿主失败记录到控制台。只接收一个值，不提供 console.log 的多参数格式化功能。

```javascript
// @grant GM.log
(async function () {
    await GM.log({ script: GM.info.name, version: GM.info.version });
})().catch(console.error);
```

<a id="values-and-change-listeners"></a>

## 存储与值变更监听

存储按安装实例的 ScriptKey 隔离，同一安装实例的活动标签页/框架共享，与页面 localStorage 无关。重新安装成另一个实例不保证复用数据。Demo 更新现有安装时保留数据，卸载时删除；其他宿主可以选择不同持久化策略。

键为字符串，允许空字符串。支持存储 undefined、null、字符串、数字（包括 NaN 和正负无穷）、布尔值、数组及对象。已存储的 undefined 表示键存在：它出现在 listValues 中，读取时不会回退到默认值。只有 deleteValue/deleteValues 才会删除键。

嵌套值遵循已核查的 Chromium Tampermonkey 5.5.1 行为：对象的 undefined 属性被省略，数组中的 undefined 和空位转为 null，嵌套非有限数转为 null。顶层负零转为零。对象仅序列化自身可枚举的字符串属性，不调用 toJSON，因此 Date 转为 {}。存储对象的读写均生成独立副本，修改后需显式保存。函数、symbol、BigInt 和循环引用不支持存储，嵌套出现时也会报错。

默认值保留在 JavaScript 端，仅在键不存在时返回。默认值可以是任意类型，包括函数、symbol、BigInt 或循环对象，并保留原引用。批量默认值在调用时读取各属性，再进行异步查询。批量默认值对象中的显式 undefined 属性也会包含在结果中。Promise 兑现仍遵循 JavaScript 对 thenable 的通常处理。

| 签名 | 参数与行为 | 返回值 |
| --- | --- | --- |
| `GM.getValue(key, defaultValue?)` | 键存在时返回存储值，否则返回默认值；未提供默认值则返回 undefined。 | `Promise<value>` |
| `GM.setValue(key, value)` | 先编码并复制值，再排队提交。 | `Promise<undefined>` |
| `GM.deleteValue(key)` | 存在时删除，不存在时无操作。 | `Promise<undefined>` |
| `GM.getValues(keysOrDefaults?)` | 传键数组时省略不存在的键，传默认值对象时补全缺失项。省略、null 或 undefined 表示读取全部；[] 和 {} 返回空对象。结果对象没有原型。 | `Promise<object>` |
| `GM.setValues(values)` | 写入对象自身可枚举的字符串键；修改存储前校验并编码全部值。 | `Promise<undefined>` |
| `GM.deleteValues(keys)` | 删除数组中的多个键，忽略重复键和不存在的键。 | `Promise<undefined>` |
| `GM.listValues()` | 按 JavaScript 属性枚举顺序列举：数组索引键在前，其余键按插入顺序排列。覆盖不改变位置，删除再创建会移到非索引键末尾。 | `Promise<string[]>` |
| `GM.addValueChangeListener(key, callback)` | 注册 callback(key, oldValue, newValue, remote)；缺失或已删除值为 undefined。宿主注册失败时拒绝 Promise。 | `Promise<number>` |
| `GM.removeValueChangeListener(listenerId)` | 移除当前执行实例的正整数监听器 ID；未知 ID 无操作。 | `Promise<undefined>` |

现代存储按家族授权，旧式全局需要精确声明。监听范围为同一安装实例的活动执行。每个已提交变更通知一次：来源执行收到 remote: false，其他执行或宿主修改时为 true。该行为适用于有镜像、无镜像。相同编码写入和删除不存在键不通知；创建或删除已存储 undefined 会通知。回调在提交后执行，也可能晚于写入 Promise；执行释放时清理监听器。

旧式签名使用相同参数：GM_getValue/GM_getValues/GM_listValues 同步返回值；GM_setValue/GM_deleteValue/GM_setValues/GM_deleteValues/GM_removeValueChangeListener 返回 undefined；GM_addValueChangeListener 立即返回数字 ID。同步值读写需要完整存储镜像，监听别名不依赖镜像。写入立即修改镜像，再串行异步提交；失败时回滚并重新同步，产生 MSC413_COMPATIBILITY_MUTATION_FAILED。同步返回成功不代表持久化完成。旧式监听注册失败时会移除本地处理器，并异步报告失败。

现代写入共享提交队列，通过 Promise 观察宿主提交成功或失败。每次批量写入或删除仅发送一个桥接请求，再逐键提交，不保证整批原子性：中途失败会停止后续操作，保留已提交的键及对应通知。参数校验失败不会修改任何键。失败后重新同步镜像并重放后续排队操作。空批次合法。

```javascript
// @grant GM.getValues
// @grant GM.setValues
// @grant GM.deleteValues
(async function () {
    await GM.setValues({ theme: 'dark', optional: undefined });
    const settings = await GM.getValues({ theme: 'light', optional: 'fallback', absent: undefined });
    console.log(settings.optional === undefined, Object.prototype.hasOwnProperty.call(settings, 'absent')); // true, true
    await GM.deleteValues(['theme', 'optional']);
})().catch(console.error);
```

**与 Tampermonkey 的关系：** 值转换、默认值和存储 API 返回类型遵循[官方参考](https://www.tampermonkey.net/documentation.php?q=GM_values)及已核查的 5.5.1 门面。MonkeySharp 保留宿主提交确认，在成功提交后通知；Tampermonkey 则立即更新本地状态并通知。启动快照限额及对不支持存储类型的明确拒绝仍是 MonkeySharp 的约束。

```javascript
// @grant GM.getValue
// @grant GM.setValue
// @grant GM.deleteValue
// @grant GM.listValues
// @grant GM.addValueChangeListener
// @grant GM.removeValueChangeListener
(async function () {
    const id = await GM.addValueChangeListener("settings", (key, oldValue, newValue, remote) => {
        console.log(key, oldValue, newValue, remote);
    });
    const settings = await GM.getValue("settings", { enabled: true });
    settings.enabled = !settings.enabled;
    await GM.setValue("settings", settings);
    console.log(await GM.listValues());
    await GM.removeValueChangeListener(id);
    await GM.deleteValue("settings");
})().catch(console.error);
```

<a id="dom-helpers"></a>

## DOM 辅助方法

### GM.addStyle(css) / GM_addStyle(css)

需要对应授权。css 是字符串。创建 style 元素，设置 textContent，并追加到 document.head 或 document.documentElement。现代返回 `Promise<HTMLStyleElement>`；旧版立即返回该元素。可以用 element.remove() 移除。非法参数分别导致 Promise 拒绝或同步抛出；DOM 错误继续传播。

### GM.addElement(tagName, attributes?) / GM.addElement(parent, tagName, attributes?)

旧版为相同重载的 GM_addElement；需要 GM.addElement 或其别名授权。tagName 是非空字符串。parent 必须具有 appendChild；省略时使用 document.body 或 document.documentElement。

attributes 是可选对象：textContent/innerHTML 设置内容，字符串 style 设置 style.cssText，已有 DOM 属性（包括函数事件处理器）直接赋值，其他字段转换成字符串后使用 setAttribute。现代返回 `Promise<Element>`，旧版立即返回 Element。非法输入和 DOM 失败会拒绝/抛出，而非返回 null。

**与 Tampermonkey 的差异：** 这些是普通页面 DOM 操作，不提供扩展级 CSP 绕过。省略 addElement 父节点时优先使用 body，不应假定与 [Tampermonkey 辅助方法的插入位置及错误行为](https://www.tampermonkey.net/documentation.php?q=GM_addElement)相同。

```javascript
// @grant GM.addStyle
// @grant GM.addElement
(async function () {
    const style = await GM.addStyle(".ms-button { color: darkgreen; }");
    const button = await GM.addElement(document.body, "button", {
        className: "ms-button", textContent: "Remove style", onclick: () => style.remove()
    });
    console.log(button);
})().catch(console.error);
```

<a id="resources"></a>

## 资源

### GM.getResourceText(name) / GM_getResourceText(name)

需要对应授权、资源提供器和 @resource name url。name 是非空、区分大小写的字符串。现代返回 `Promise<string>`，使用提供器给出的文本，否则将字节按 UTF-8 解码。旧版从完整启动资源快照立即返回字符串。未声明的资源导致拒绝；缺少旧版快照时不暴露别名，在可用快照中找不到名称时抛出错误。

### GM.getResourceUrl(name) / GM.getResourceUrl(name) / GM_getResourceURL(name)

前提和名称规则相同，但需要自己的授权。现代返回 `Promise<string>`，内容为 **data URL**：`data:<media-type>;base64,<bytes>`；旧版从快照立即返回该 URL。它不是原始下载地址，也不是需要 revoke 的对象 URL。不支持 isBlobUrl 参数。两种资源 API 均应用完整性检查和大小限制。

现代名称仅为 `GM.getResourceUrl`，官方旧式名称为 `GM_getResourceURL`。资源按现代家族授权；旧式全局需精确声明和完整快照。不提供 `GM.getResourceURL`。

请按 data URL 处理，不应假定其他管理器的扩展 URL、对象 URL 或附加选项，参见[官方资源参考](https://www.tampermonkey.net/documentation.php?q=GM_getResource)。

请将下面片段中的 badge URL 替换为真实可访问的图片资源；示例域名不保证该路径存在 favicon。

```javascript
// @resource badge https://example.com/favicon.ico
// @grant GM.getResourceUrl
(async function () {
    const image = document.createElement("img");
    image.src = await GM.getResourceUrl("badge");
    document.body.appendChild(image);
})().catch(console.error);
```

文本资源可声明 @resource config ...，授予 GM.getResourceText，再在 async 入口中使用 `const text = await GM.getResourceText("config")`。JSON 解析由脚本自己完成。

<a id="http-requests"></a>

## HTTP 请求

### GM.xmlHttpRequest(details) / GM_xmlhttpRequest(details)

精确授权 GM.xmlHttpRequest（大写 H）或 GM_xmlhttpRequest，并为每个目标声明 @connect。需要 HTTP 服务；CefSharp 默认实现使用浏览器请求上下文，非匿名时可以使用适用的 Cookie/凭据。这条宿主请求路径独立于页面 fetch 及其 CORS 限制，但仍受授权和宿主策略约束。

现代返回带立即可用 .abort() 方法的 `Promise<Response>`。非流式响应在组装完主体后兑现；流式响应在元数据/流可用时兑现，之后仍可能完成或失败。旧版立即返回 {abort()} 句柄，通过回调接收响应。abort 没有有用的返回值。回调异常记录到日志。HTTP 4xx/5xx **仍会兑现 Promise 并调用 onload**，必须自行检查 status。

### 请求参数

| details 字段 | 类型 / 默认值 | 行为 |
| --- | --- | --- |
| `url` | 必需，绝对 HTTP(S) 字符串或 URL 对象 | 不支持相对 URL 和 Blob/File URL。 |
| `method` | 字符串，"GET" | 有效 HTTP token，不区分大小写；禁止 CONNECT、TRACE、TRACK。 |
| `headers` | 字符串到字符串的对象，{} | 校验头名称和值，禁止 CR/LF 注入。 |
| `data` | 可选主体 | 字符串、ArrayBuffer、类型化数组、DataView、Blob/File、FormData、URLSearchParams、对象或数组；null/省略表示无主体。 |
| `binary` | 布尔值，false | 必须使用字符串主体；每个 UTF-16 码元取低 8 位发送，普通字符串按 UTF-8 编码。 |
| `responseType` | 字符串，"text" | text、json、arraybuffer、blob、document、xml 或 stream；空字符串视为 text。 |
| `responseXML` | 布尔值，false | true 时覆盖 responseType 为 document，通过页面 DOMParser 按 XML 解析。 |
| `timeout` | 非负整数，毫秒 | 0/省略表示不提供单独的 XHR 截止时间；fetch:true 时忽略，宿主仍可设置限制。 |
| `cookie` | 可选字符串 | 显式 Cookie 头输入。 |
| `user`、`password` | 可选字符串 | 传给宿主 HTTP 服务的认证凭据。 |
| `anonymous` | 布尔值，false | 禁用浏览器已存储的 Cookie 和凭据。 |
| `overrideMimeType` | 可选字符串 | 覆盖响应 MIME/字符集处理。 |
| `redirect` | "follow"（默认）、"error"、"manual" | 跟随重定向、报错或返回而不跟随；每个跟随目标重新授权。 |
| `nocache`、`revalidate` | 布尔值，false | 传给 HTTP 服务的缓存控制请求。 |
| `fetch` | 布尔值，false | 兼容模式：忽略 timeout/进度，只暴露 readyState 4；并非切换成页面 fetch。 |
| `context` | 任意 JavaScript 值 | 在本地作为相同的 response.context 返回，不经过宿主序列化。 |
| `signal` | 可选 AbortSignal | 取消操作；取消响应流也会中止操作。 |
| 回调字段 | 可选函数 | 见回调表；也接受 upload.onprogress，优先于 onuploadprogress。 |
| `proxy`、`cookiePartition` | 不支持 | 提供任一个都会返回/抛出 MSP006_NOT_SUPPORTED。 |
| `synchronous:true`、`async:false` | 不支持 | 以 MSP006_NOT_SUPPORTED 拒绝；只支持异步请求。 |

对象/数组按 JSON 发送，URLSearchParams 使用表单编码，FormData 使用包含自动边界、文件名和媒体类型的 multipart。未自行设置时会补入适当 Content-Type。支持 Blob/File **请求体**，但不支持 Blob/File **URL**。

允许 User-Agent、Referer、Origin 和 Cookie 头。禁止 Host、Content-Length、连接/传输控制头，以及 Sec-*/Proxy-* 头。跨源重定向会移除显式 Cookie 和 Authorization。Cookie 合并及浏览器认证细节仍由宿主服务决定，不应假定与每个 Tampermonkey 浏览器版本完全一致。

@connect self 同时检查协议、主机和端口。`example.com` 和 `*.example.com` 均允许裸域名自身及其所有层级的子域名；IP 和 localhost 必须精确匹配。URL 声明还限制协议/端口，但其中路径不构成白名单限制。没有白名单时，同源 XHR 也被拒绝。原始目标和所有重定向目标都必须通过白名单及宿主权限策略。

### 响应与回调

| 响应字段 | 含义 |
| --- | --- |
| `finalUrl` | 最终获准访问的 URL。 |
| `readyState` | 初始为 0，随后为 1–4；fetch 模式只报告 4。 |
| `status`、`statusText` | HTTP 状态及描述，初始为 0/空字符串。 |
| `responseHeaders` | 原始头字符串，保留重复响应头。 |
| `response` | 文本；解析后的 JSON（空或非法 JSON 为 null）；ArrayBuffer；Blob；document 对应 XML Document；xml 对应文本；stream 对应 Uint8Array 数据块的 ReadableStream。 |
| `responseText` | text/JSON/document/xml 的解码文本；arraybuffer/blob/stream 为 undefined。进度回调不保证提供逐步接收的文本主体。 |
| `responseXML` | document/xml 类型下 DOMParser 可用时的 XML Document，否则为 null。无效 XML 可能产生包含解析错误的文档。 |
| `context` | 原始 JavaScript context 值。 |

| 回调 | 参数 / 时机 |
| --- | --- |
| `onloadstart`、`onreadystatechange` | 生命周期通知到达时的当前响应状态。 |
| `onprogress`、`onuploadprogress`、`upload.onprogress` | 状态加 loaded、total（未知时为 0）、lengthComputable；fetch 模式不调用。 |
| `onload` | 完整响应状态，HTTP 错误状态也使用此回调。 |
| `onerror` | 网络、权限、序列化或桥接失败的结构化错误 {error, details, code, name}。 |
| `ontimeout`、`onabort` | 超时或取消错误信息。 |
| `onloadend` | 成功时为最终状态，失败时为错误信息。 |

回调的 this 指向响应状态。现代方法的前置校验可能在返回 Promise 前同步抛出，应将整个调用与 await 放进 try/catch。启动后失败会拒绝 Promise，可读取可用的 .code/.name。回调错误类别包括 not_supported、not_permitted、timeout 和 not_succeeded。旧版门面尽可能将即时请求校验失败传给 onerror，但非法回调类型仍可能直接抛出。

XHR 主体分块传输；默认宿主不设置整体请求/响应字节上限，前 256 KiB 保存在内存，之后暂存到临时文件。这独立于普通桥接消息限制。JavaScript 侧仍会在内存中组装非流式响应，大响应优先使用 stream。

**与 Tampermonkey 的差异：** 传输由 CefSharp/宿主实现；不支持代理和分区 Cookie 控制；DOMParser 的 XML/document 类型及 upload/signal 控制遵循 MonkeySharp 行为。redirect 模式不会自动启用 fetch；进度回调不保证逐步解码的 responseText。两者当前文档都不支持同步请求，不应将这一共同限制写成 MonkeySharp 特有差异。参见[官方 XHR 参考](https://www.tampermonkey.net/documentation.php?q=GM_xmlhttpRequest)。

```javascript
// @grant GM.xmlHttpRequest
// @connect example.com
(async function () {
    const request = GM.xmlHttpRequest({
        url: "https://example.com/", responseType: "text", timeout: 15000,
        onprogress: progress => console.log(progress.loaded, progress.total)
    });
    // To cancel while pending: request.abort();
    const response = await request;
    if (response.status < 200 || response.status >= 300) {
        throw new Error(`HTTP ${response.status}`);
    }
    console.log(response.finalUrl, response.responseText);
})().catch(console.error);
```

<a id="menus-notifications-and-clipboard"></a>

## 菜单、通知与剪贴板

### GM.registerMenuCommand(name, callback, options_or_accessKey?)

需要菜单授权及服务。第三参数可以是访问键字符串，或 `{id, accessKey, title, autoClose}`。id 是正整数或非空字符串，用于更新当前执行的已有注册，不存在则创建；省略时分配数字 ID。现代返回注册后的 `Promise<number|string>`，旧版立即返回 ID。更新和注销按调用顺序排队。回调收到渲染器创建的 MouseEvent（type 为 click），不包含原生鼠标坐标或键盘修饰键；回调异常单独报告。

Demo 将同一安装、同一标签页中 name/title/accessKey 相同的条目合并，点击时调用所有已订阅 frame 的回调。title 显示为提示，autoClose:false 保持菜单打开；合并项有任一订阅要求保持时即保持。注销或结束一个 frame 仅移除该 frame 的订阅，执行结束清理其命令。固定的 5.5.1 契约不支持后续版本的 clickedFrame 选项，会明确拒绝。

### GM.unregisterMenuCommand(commandId)

接受数字或字符串 ID。现代兑现 undefined，旧版立即返回 undefined；未知 ID 无副作用，不返回自有移除布尔值。现代菜单家族同时提供注册及注销，旧版全局仍按准确声明暴露。

```javascript
const id = await GM.registerMenuCommand('操作', event => console.log(event.type), {title:'执行操作',autoClose:false});
await GM.registerMenuCommand('更新操作', () => {}, {id});
await GM.unregisterMenuCommand(id);
```

### GM.notification(details, ondone?)

两种入口均接受 `(details, ondone?)` 或 `(text, title?, image?, onclick?)`，也支持 `(text, ondone)`。现代返回等待交互结束的 `Promise<boolean>`：点击为 true，关闭、超时、替换或仅高亮完成为 false；旧版返回 undefined。ondone 先于 Promise 结算。宿主显示前已登记回调，重复点击和关闭不能重复结算。

选项包括 text（除非 highlight 否则必需）、title（默认脚本名）、image/imageUrl、tag、url、highlight、silent、timeout（非负整数毫秒）、onclick 和 ondone。相同非空 tag 替换该安装在当前宿主中的通知。onclick 收到可取消的 click Event，同步调用 event.preventDefault() 阻止默认 URL 跳转。回调异常单独报告，仍确认点击；宿主等待确认后再执行默认动作，五秒没有确认时按默认动作处理。URL 必须是经过宿主授权的绝对 HTTP(S) 地址。

Demo 使用 WPF 所属窗口，highlight 选择并激活来源标签；无 text 的高亮直接完成。silent 禁止声音，正数 timeout 到期关闭。普通 URL 点击在回调后打开标签。无 url/tag 的通知在文档结束时关闭；带 url/tag 的通知保留到完成或宿主释放，旧文档回调停止，默认点击动作仍可执行。tag 限于同一宿主中的脚本安装，不是全局系统通知服务。

```javascript
const clicked = await GM.notification({text:'已就绪',tag:'status',silent:true,url:'https://example.com/',
    onclick:event => event.preventDefault(), ondone:() => console.log('已关闭')});
console.log('点击:', clicked);
```

### GM.setClipboard(data, info?, callback?) / GM_setClipboard(data, info?, callback?)

data 必须是字符串。info 接受 text、html、text/plain、text/html，或 `{type:'text'|'html', mimetype:'text/plain'|'text/html'}`，mimetype 优先，默认文本；其他格式在写入前拒绝。可选 callback 只在实际写入成功后调用。现代在写入和回调后兑现 undefined，旧版立即返回 undefined。回调异常不会撤销写入或使现代调用拒绝。

Demo 在 STA dispatcher 写入 Unicode 文本和 HTML 的 CF_HTML 格式，偏移按 UTF-8 字节计算，保留占用重试。HTML 保持原格式，不静默降为纯文本。真实 E2E 检查两种格式，并在检查后恢复原剪贴板。

```javascript
await GM.setClipboard('<b>你好</b>', {type:'html',mimetype:'text/html'});
GM_setClipboard('已复制', 'text', () => console.log('已写入'));
```

<a id="tabs-and-tab-state"></a>

## 标签页及标签页状态

### GM.openInTab(url, options?) / GM_openInTab(url, options?)

需要对应授权和标签页服务。url 是绝对 HTTP(S) 字符串。现代 options 支持布尔值 active（默认 **false**）、insert（false）、setParent（false）。insert:true 请求插入到来源标签页旁边，布局由宿主决定。

**两种形式**都立即返回本地句柄：

| 句柄成员 | 行为 |
| --- | --- |
| `id` | 初始为 null，打开后异步填入宿主字符串 ID。 |
| `closed` | 初始 false，收到宿主关闭通知后变为 true。 |
| `onclose` | 可以赋值函数，关闭时无参数调用。 |
| `close()` | `Promise<boolean>`；过早调用会排队等到打开获得 ID。重复调用复用待处理结果或返回 false。 |

打开失败会记录日志，或拒绝待处理的关闭请求；没有等待打开就绪的 Promise。执行实例释放时销毁句柄，但不一定关闭已打开的用户标签页；Demo 会保留它们。

旧版还接受 GM_openInTab(url, loadInBackgroundBoolean) 及 options.loadInBackground；没有显式提供 active 时，设置 `active = !loadInBackground`，现代调用不进行这些转换。不支持 incognito 或数字插入位置。默认不激活的行为与[官方标签页 API](https://www.tampermonkey.net/documentation.php?q=GM_openInTab)一致，不属于兼容差异。

### GM.getTab()、GM.saveTab(value)、GM.getTabs()

各自需要对应授权和标签页状态服务。数据按安装实例关联**当前存活的宿主标签页**，不是页面 sessionStorage，也不是枚举任意浏览器标签页。

| API | 参数 / 行为 | 现代返回值 | 旧版约定 |
| --- | --- | --- | --- |
| `GM.getTab()` | 读取本脚本当前标签页对象。 | `Promise<object>` | GM_getTab(callback) 返回 undefined；回调为 (value, null) 或 (null, error)。 |
| `GM.saveTab(value)` | 必需 JSON 对象，替换状态；拒绝数组和基础类型。 | `Promise<undefined>` | GM_saveTab(value) 返回 undefined，失败记录日志；无完成回调重载。 |
| `GM.getTabs()` | 读取本脚本存活标签页的状态映射，以宿主标签页 ID 为键。 | `Promise<object>` | GM_getTabs(callback) 返回 undefined，回调/错误约定同 getTab。 |

旧版 getTab/getTabs 错误为结构化 {error, details, code} 对象。修改 getTab 返回的对象不会自动保存。Demo 初始状态为 {}，完整页面导航后保留状态，关闭标签页后删除；应用启动时清除过期状态，不恢复浏览器会话。

```javascript
// @grant GM.openInTab
// @grant GM.getTab
// @grant GM.saveTab
// @grant GM.getTabs
(async function () {
    const state = await GM.getTab();
    state.lastAction = "opened help";
    await GM.saveTab(state);
    console.log(await GM.getTabs());
    const tab = GM.openInTab("https://example.com/", { active: true, setParent: true });
    tab.onclose = () => console.log("Help tab closed");
    // Later, while this execution is active: await tab.close();
})().catch(console.error);
```

<a id="downloads"></a>

## 下载

### GM.download(details) / GM_download(details)

需要对应授权和下载服务。两种形式都接受选项对象或 `(url, name?)`；url 必须是绝对 HTTP(S) 字符串。Blob/File 输入仍不支持。

| 字段 | 类型 / 默认值 | 行为 |
| --- | --- | --- |
| `url` | 必填 HTTP(S) 字符串 | 初始目标及重定向均经过宿主授权。 |
| `name` | 可选字符串 | 建议文件名，最终路径由宿主选择及清理。 |
| `headers` | 字符串字典，`{}` | 使用共用的浏览器网络服务。 |
| `anonymous` | 布尔值，false | 不带浏览器 Cookie 和凭据。 |
| `saveAs` | 布尔值，false | 请求宿主保存对话框。 |
| `conflictAction` | uniquify / overwrite / prompt | 默认 uniquify；保存和冲突选项需要 browser 模式。 |
| `timeout` | 正整数毫秒 | 覆盖选择路径、传输及提交；文件服务默认十分钟。 |
| `onload` | 函数 `({url, name, loaded})` | 最终文件提交成功后调用。 |
| `onprogress` | 函数 `({loaded, total})` | 字节进度；total 可为 null。 |
| `onerror` | 函数 `({error, details, code?})` | 启动、传输、取消及磁盘失败。 |
| `ontimeout` | 函数 | 超时；缺省回退到 onerror。 |

现代 GM.download 立即返回**带 abort() 的原始完成 Promise**，只有文件 flush、关闭并原子移动或替换到最终位置后才兑现。支持的完成详情是 `{url, name, loaded}` 子集：最终 URL、实际文件名和提交字节数，不含本地路径和内部 ID。旧版立即返回 `{abort()}`。两者 abort 均返回 undefined，在宿主确认前也能取消，终态后调用无害。两者均支持回调；回调异常单独报告，不会把已提交的下载变成失败。每次操作只结算一次。

错误使用 `{error, details, code?}`，类别包括 not_enabled、not_permitted、not_whitelisted、not_supported、not_succeeded 和 timeout。现代 Promise 拒绝并调用对应回调；旧版通过回调报告。保存取消和主动中止归为 not_succeeded。Blob/File 和无效选项在网络操作前拒绝。

Demo GM 下载与 GM XHR 使用同一个 CEF HTTP 服务及 RequestContext，继承浏览器登录凭据、匿名语义、重定向授权及配置的 webRequest 组合。内容通过有界缓冲流入临时文件，再提交到 Data/Downloads 或用户选择的位置；失败和取消先清理临时文件，再通知终态。下载由宿主策略授权，不应用 XHR 的 @connect 列表，也没有扩展名白名单偏好设置。

已接受的下载属于宿主任务。导航、执行结束、脚本禁用或卸载会停止文档回调，已接受的任务按捕获的授权和重定向策略继续。宿主释放时取消任务，并应在关闭 CEF/网络服务前等待清理完成。完成前显式 abort 会取消任务；未接受或无效调用不启动传输。

```javascript
// @grant GM.download
const pending = GM.download({ url: 'https://example.com/file', name: 'file.bin' });
// pending.abort(); // 可在调用后立即取消
pending.then(details => console.log('已提交:', details.name, details.loaded), console.error);

// @grant GM_download
const handle = GM_download({
    url: 'https://example.com/file', name: 'file.bin',
    onload: details => console.log('已提交:', details.name),
    onerror: error => console.error(error)
});
// handle.abort();
```

## Cookie

### GM.cookie / GM_cookie

需要 Cookie 授权与服务。现代 GM.cookie 仅提供 list/set/delete，旧版 GM_cookie 是带这三个方法的函数命名空间；没有 addListener/removeListener 自有扩展。访问后端前检查目标 URL 的 match/include/exclude 规则与宿主策略，@connect 不能替代这些检查。返回值还会在 Core 按 URL、host-only/domain、path 和 secure 范围再次过滤。

| 方法 | 现代结果 | 旧版结果 / 回调 |
| --- | --- | --- |
| list(details?) | 实际 Cookie[] 的 Promise | undefined；callback(cookies, null)，失败为 callback(null, errorString) |
| set(details) | 验证写入后兑现 undefined | undefined；callback() 或 callback(errorString) |
| delete(details) | 验证删除后兑现 undefined | undefined；callback() 或 callback(errorString) |

省略 details/url 使用当前 frame URL。name 是可选精确列表过滤条件，set/delete 要求非空字符串。domain/path 是可选精确列表过滤条件。set 不传 domain 创建 host-only Cookie；显式 domain 必须覆盖目标 host，并在 Chromium 接受时创建 domain Cookie。默认 path 是 URL 所在目录（/nested/page 对应 /nested），与固定参考一致；显式路径以 / 开始。value 默认空字符串。expirationDate 是可选整数 Unix 秒，省略即 session Cookie。secure/httpOnly 默认 false。sameSite 接受 unspecified、no_restriction、lax、strict，实际映射到 CDP，而非回显输入。

列表返回真实 name/value/domain/path、hostOnly、session、secure、httpOnly 及规范化 sameSite；持久 Cookie 含 Unix 秒 expirationDate，session Cookie 省略该字段。未知 first-party/partition 状态不伪造。set 检查协议结果，再读回完整身份及属性；策略拒绝或静默丢弃都报错。过期时间在过去的 set 验证 Cookie 已不存在。delete 选择一个可见身份（路径最长优先，同长时 host-only 优先），按准确 domain/path 删除并验证不存在；显式 domain/path 进一步限定，不会按名字批量误删。删除不存在项无副作用。

默认后端使用宿主共享 CDP 会话，对应已附加浏览器的真实 RequestContext。单次操作始终使用同一会话，不解除其他消费者的观察器。导航可取消文档调用，detach/dispose 使原生会话失效。直接宿主可使用 new CefSharpCookieService(host)，旧的仅 RequestContext 构造器已移除。自定义 ICookieService 仅实现 CRUD，并须提供经过验证的实际状态。

partitionKey、cookiePartition、firstPartyDomain 在当前所有版本（包括 84）都于副作用前明确拒绝，分区支持保留给后续步骤。未分区后端过滤掉分区 Cookie，避免误改。旧版回调异常只单独报告一次，不改变已完成操作。请使用旧版命名空间的三个公开方法，不支持直接调用命名空间本身。

```javascript
await GM.cookie.set({name:'session',value:'ready',sameSite:'lax',path:'/'});
console.log(await GM.cookie.list({name:'session'}));
await GM.cookie.delete({name:'session'}); // undefined，不返回删除布尔值
GM_cookie.list({}, (cookies, error) => console.log(cookies, error));
```

<a id="request-interception"></a>

## 请求拦截

### GM.webRequest(rules, listener?) / GM_webRequest(rules, listener?)

授权 GM.webRequest 或 GM_webRequest，需要 webRequest 服务，CefSharp 默认提供。rules 可以是单个规则对象或数组，每项包含 selector 和 action。注册彼此独立，执行结束时释放；新建注册不会替换之前的句柄。

| 输入 | 支持的格式 |
| --- | --- |
| 字符串 selector | URL 通配符、match 风格模式或斜杠包围的 .NET 正则。 |
| 对象 selector | include、match、exclude 各接受字符串或字符串数组；排除优先，include/match 是或关系，也允许只给 exclude。 |
| `action: "cancel"` | 取消匹配请求。 |
| `action: "https://example.com/new"` | 静态 HTTP(S) 重定向。 |
| `action: {from: "...", to: "..."}` | 将 URL 中首次出现且区分大小写的字面 from 替换为 to，不解析正则或展开捕获；两个字符串都必须非空。 |
| `listener(info, message, details)` | 可选函数，观察取消/重定向匹配；返回值不能改变网络决定。 |

重定向目标必须通过脚本正向 URL 规则及排除规则；静态目标在注册时检查，执行时也会检查。计算出的目标无效或不满足规则时，默认服务会**阻止该请求**。应使用范围明确的 selector。默认适配器评估浏览器资源回调，没有 Tampermonkey 文档所述的四种资源类型限制。MonkeySharp 默认服务中，数组靠后的规则优先级更高。已经发生的请求不能补拦截。

两种形式都立即返回 {id, remove()}。id 初始为 null，之后变为宿主字符串；remove() 返回 `Promise<boolean>`，可在注册完成前调用。没有 listener 时不允许空规则。注册失败记录日志，或拒绝已经等待的移除请求。

回调参数为：

- info：{requestId, url, method, type}。
- message："cancel" 或 "redirect"。
- details：{phase, redirectUrl, error}，具体值取决于请求通知。这是匹配规则的观察信息，不代表重定向后的请求已成功加载。

**与 Tampermonkey 的差异：**[官方 API](https://www.tampermonkey.net/documentation.php?q=GM_webRequest)使用 {cancel:true}/{redirect: ...} 动作对象、基于正则的重定向替换，以及 (actionString, "ok"|"error", details) 回调。这些**不是** MonkeySharp 的约定，必须使用上表中的字面替换、动作格式和回调对象。MonkeySharp 增加了可移除句柄，不自动应用静态 @webRequest，脚本门面也不暴露任意头修改/认证动作。Tampermonkey 自身还受扩展清单和浏览器版本影响，不能据此推断 MonkeySharp 的可用性。

```javascript
// @grant GM.webRequest
// The script also needs @match https://example.com/* for these redirect targets.
const registration = GM.webRequest([
    { selector: "https://example.com/tracker/*", action: "cancel" },
    { selector: "https://example.com/old/*", action: { from: "/old/", to: "/new/" } }
], (info, message, details) => {
    console.log(info.url, message, details.redirectUrl);
});
// Later: registration.remove().catch(console.error);
```

<a id="window-extensions"></a>

## window 扩展

| 能力 | 参数与行为 | 返回值 |
| --- | --- | --- |
| `unsafeWindow` | 精确授权，暴露页面全局对象；是值而非函数。 | 页面全局对象。 |
| `window.close()` | 精确 window.close 授权及窗口服务，请求宿主关闭来源标签页/窗口。 | `undefined`，宿主政策可能拒绝操作。 |
| `window.focus()` | 精确 window.focus 授权及窗口服务，请求宿主聚焦来源标签页/窗口。 | `undefined`，宿主政策可能拒绝操作。 |
| `window.onurlchange` | 精确授权，可赋值函数/null，或 window.addEventListener("urlchange", handler)。 | 属性/事件注册，回调接收 {url, oldURL}。 |

URL 变化时会观察 pushState、replaceState、popstate 和 hashchange。使用 window.removeEventListener("urlchange", handler) 移除同一函数。回调的 this 为 window 门面；参数是普通对象，不是原生 Event，once/capture 等选项不会改变这套自定义注册行为。没有授权就不会建立 MonkeySharp URL 变更订阅。close/focus 缺少授权或服务时不可用。

脚本中的 window 是绑定原生函数到页面窗口的门面。close/focus 返回 undefined，不提供 GM.windowClose/GM.windowFocus 别名。

**与 Tampermonkey 的差异：** 这里的 unsafeWindow 不跨越扩展隔离世界；close/focus 是异步宿主决定。不应从[官方 window API](https://www.tampermonkey.net/documentation.php?q=window)推断存在原生 Event 或扩展隔离。

```javascript
// @grant window.onurlchange
window.onurlchange = change => console.log(change.oldURL, "->", change.url);
const onRoute = change => console.log("Current route:", change.url);
window.addEventListener("urlchange", onRoute);
// Later: window.removeEventListener("urlchange", onRoute);
```

<a id="tampermonkey-compatibility-checklist"></a>

## Tampermonkey 兼容性检查表

| 方面 | 迁移时的处理 |
| --- | --- |
| 执行边界 | 将脚本和页面视为可信的页面世界代码，不具有扩展沙箱或保证的 CSP 绕过。 |
| 授权 | info 默认可用，旧式全局需要精确声明；现代 API 家族按固定 5.5.1 实测开放。 |
| 时机 / SPA | 不依赖保证的 document-start 或历史导航后的自动重跑。 |
| 子域名匹配 | `*.domain` 的 match 模式包含裸域名及其所有层级的子域名。 |
| 存储 | 使用支持的存储值，await 现代写入以观察失败，并考虑镜像限额及提交后通知的时序。 |
| 资源 | 使用 `GM.getResourceUrl` 或 `GM.getResourceUrl`，声明任一对应 grant，按 data URL 处理，并确认宿主资源提供器可用。 |
| XHR | 检查代理/分区/Blob URL 等不支持项，适配进度/XML 行为，显式检查 HTTP 状态。 |
| 菜单 / 剪贴板 | 菜单选项及 ID 更新、通知交互完成、文本/HTML 剪贴板 info 与回调重载。 |
| 通知 | 只使用支持字段；现代 Promise 在注册后兑现，而非关闭后。 |
| 标签页 | 两种形式都立即返回句柄；通过 close/onclose 操作，不支持隐私模式和数字插入位置。 |
| 下载 | 立即返回带 abort 的完成 Promise；Demo 共用浏览器网络上下文，文件提交后才完成。 |
| Cookie | 没有 partitionKey 语义，默认 SameSite/第一方写入及变更订阅有限。 |
| webRequest | 改写动作对象及回调，显式注册运行时规则并保留移除句柄。 |
| 其他管理器 API | 不内置 GM.audio/GM_audio 和扩展专属内容/部署 API；未知授权不会增加它们。 |

这张表描述支持的约定及已知差异，不保证任意 Tampermonkey 脚本都能原样运行。应在实际宿主应用中验证所用功能。

<a id="errors-and-troubleshooting"></a>

## 错误与排查

大多数现代桥接操作以带 .code/.message 的错误拒绝，本地校验也可能抛出/拒绝 TypeError。现代下载的宿主/启动失败则以不带 .message 的普通 {error, details, code} 对象拒绝。XHR 和旧版下载使用各自的结构化回调错误，旧版 Cookie 错误是字符串，旧版标签页读取错误是对象。旧版不等待结果的操作通常记录失败，不会将异步错误同步抛给调用者。

| 错误代码 | 含义 / 处理 |
| --- | --- |
| `MSP001_PROTOCOL_VERSION`、`MSP002_MALFORMED_MESSAGE` | 桥接/版本不一致或消息格式错误，反馈给应用维护者。 |
| `MSP003_SESSION_EXPIRED` | 文档/执行已失效，导航后不要继续使用旧句柄。 |
| `MSP004_GRANT_DENIED` | 未授予方法权限，检查元数据精确拼写。 |
| `MSP005_PERMISSION_DENIED` | 宿主策略、XHR connect 白名单或目标 URL 规则拒绝。 |
| `MSP006_NOT_SUPPORTED` | 缺少实现/服务或参数不受支持。 |
| `MSP007_INVALID_PARAMS` | 类型错误、缺少必需字段或非法值。 |
| `MSP008_PAYLOAD_TOO_LARGE` | 超过普通桥接/资源限额。 |
| `MSP009_TIMEOUT`、`MSP010_CANCELED` | 超时或取消，应处理终态。 |
| `MSP999_INTERNAL` | 提供器/内部失败，查看宿主诊断。 |

默认普通桥接请求和响应各限制 1 MiB，资源/依赖限制 10 MiB，普通请求超时 30 秒，每个文档最多 64 个待处理调用。宿主可以修改。分块 XHR 传输及其长时间 execute 操作使用独立控制，不将 1 MiB 当作整体主体上限。启动快照另有前述限制。

| 现象 | 排查项 |
| --- | --- |
| 脚本没有执行 | 启用状态、正向匹配、排除项、框架规则、run-at、元数据错误；显式授权还需要宿主信任选项（MSR201_BRIDGE_INTEGRITY_REQUIRED）。 |
| GM 或方法为 undefined | 授权状态、大小写、兼容模式及宿主服务。缺失 API 通常根本不暴露，而非提供空实现。 |
| 只有同步存储/资源 API 缺失 | 快照选项、大小和超时；MSC410_COMPATIBILITY_BOOTSTRAP_FAILED、MSC411_COMPATIBILITY_SNAPSHOT_DEGRADED、MSC412_COMPATIBILITY_BOOTSTRAP_LIMIT。 |
| 旧版写入看似成功后又回退 | 异步提交失败，查看 MSC413_COMPATIBILITY_MUTATION_FAILED；改用可 await 的现代写入。 |
| 依赖不可用 | 检查宿主依赖提供器及 MSR400_DEPENDENCY_RESOLUTION_FAILED / MSR401_DEPENDENCY_PROVIDER_UNAVAILABLE。 |
| 执行过晚或世界不符 | document-start 尽力执行（MSR100/MSR101），sandbox/unwrap 不创建隔离（MSR212/MSR213）。 |
| XHR 重定向后失败 | @connect 必须允许每个重定向目标，并检查宿主策略。 |
| 浏览器已登录但下载失败 | 检查实际 RequestContext、anonymous 选项、重定向授权及宿主权限；GM 下载共用浏览器网络服务；如果应用提供认证下载/原生下载路径，可使用该路径。 |
| 导航后异步回调停止 | 旧执行已释放，需要在新文档重新注册。 |
| JavaScript 异常 | 查看开发者工具及宿主 MSR300_SCRIPT_EXCEPTION，显式捕获异步错误。 |

<a id="reference-sources"></a>

## 参考来源

MonkeySharp 行为依据[授权目录](../Mzying2001.MonkeySharp.Core/Compatibility/UserScriptGrantCatalog.cs)、[JavaScript 门面](../Mzying2001.MonkeySharp.Core/Js/Init.js)、[Core API 提供器](../Mzying2001.MonkeySharp.Core/Apis/)、[元数据解析器](../Mzying2001.MonkeySharp.Core/Parsing/UserScriptMetadataParser.cs)、[运行时信息构建](../Mzying2001.MonkeySharp.Core/Runtime/UserScriptEngine.cs)、[CefSharp 服务](../Mzying2001.MonkeySharp.CefSharp/)及 [JavaScript 行为测试](../Mzying2001.MonkeySharp.Core.Tests/JavaScript/)核对。具体持久化及交互服务见 [Demo 指南](../Mzying2001.MonkeySharp.Demo/README.md)。升级运行时或替换宿主服务时，可据此复核。
