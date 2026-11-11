# 增强范围交付评估

[English](m2-report.md)

第 36 步只验收本轮选定增强，不代表整份计划全部完成。[完整 M1 缺口](m1-report.zh-CN.md)及原第 15 步继续保留，第 30 步完整验收也未完成。可行性评估完成不等于实现 BFCache 恢复、严格 full-GM document-start 或 CSP 绕过。

## 本轮范围

| 步骤 | 已交付结果 | 保留边界 |
| --- | --- | --- |
| 28 | 有界增量非 fetch 文本/JSON/document/xml responseText、持续解码及单一终态 | fetch 进度与固定 beta 5.6.6242 仍有明确差异；JSON/document 仍在最终解析 |
| 29 | 授权后有界读取 ReadableStream、暂存后上传、取消/导航清理及重定向重放 | 不保证 renderer 源的原生背压或实时上传；暂存重放和 reader 释放是主动保证，不算全面 beta 等价 |
| 30 | 原生输入字段、注册代际、可信 clickedFrame 过滤及 84 isolated 子 frame 手动菜单 | 121/151 子 frame 映射和固定 beta 原生点击/重叠证据未完成；包消费者的字段验证使用宿主回调，不是原生键盘输入 |
| 31 | 可复现的原生 BFCache 可行性评估 | 没有真正命中缓存，恢复功能未实现 |
| 32 | isolated child target/session 路由、准备值/资源、带权限检查的 XHR、嵌套、进程迁移及普通/私密上下文验证 | 原生 OOPIF 菜单、严格 start、BFCache 和未单独验证的 API 范围不在保证内 |
| 33 | 独立 full-GM start/CSP 实验及 renderer 部署决策 | 严格 start 和 CSP 绕过未实现；严格要求继续在源码副作用前拒绝 |
| 34 | 本轮未纳入 | 没有取得新的原生请求分区路线；默认传输仍在发送前拒绝 cookiePartition |
| 35 | 本轮未选择新增专项 | audible、历史 MV2 参考及其他候选任务保留原有限制 |

34、35 是按原生证据、语料失败和宿主需求选择的 P2 专项。计划明确规定第 36 步只验收 28～35 中实际纳入的范围。因此从 33 进入 36 不表示 34/35 已完成或被合并；实施记录现已明确补上两步的未纳入状态。

## 验证与来源

最终矩阵验证实现提交 `1ef4c4f`，补丁仅修正索引页的中文链接文字；运行时、测试、runner、语料及参考输入均已提交。原始结果保留在 `artifacts/p2-delivery/1d2373523e2f`。

| CefSharp | 架构 | .NET | JS | SmokeHost/CDP + 普通/私密 OOPIF | WPF | NuGet/公开消费者 | 源码 + 包消费语料 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 84.4.10 | x64 | 422 通过 | 129 通过 | 通过 | 通过 | 通过 | 10 + 10，声明范围 |
| 84.4.10 | x86 | 373 通过 | 129 通过 | 通过 | 不适用 | 通过 | 10 + 10，声明范围 |
| 121.3.70 | x64 | 422 通过 | 129 通过 | 通过 | 通过 | 通过 | 10 + 10，声明范围 |
| 121.3.70 | x86 | 373 通过 | 129 通过 | 通过 | 不适用 | 通过 | 10 + 10，声明范围 |
| 151.3.240 | x64 | 422 通过 | 129 通过 | 通过 | 通过 | 通过 | 10 + 10，声明范围 |
| 151.3.240 | x86 | 373 通过 | 129 通过 | 通过 | 不适用 | 通过 | 10 + 10，声明范围 |

x64 每组包含 305 Core、68 Adapter、49 Demo 测试，x86 不含 Demo。每组均有 15 项 SmokeHost 兼容场景及普通/私密 OOPIF 记录，OOPIF 无运行时诊断。源码与包消费共 120 次语料场景保留逐项结果：84 每轮 6 passed + 4 expected-engine-limitation，121/151 每轮 8 passed + 2 expected-engine-limitation，不把这些限制计为功能通过。

最终矩阵及被验证实现身份见[版本化交付证据](../../scripts/compatibility/evidence/enhanced-follow-up-delivery-p1.json)。原始日志、包和完整原生探针记录保留在该证据指向的 artifacts 目录。使用 `./scripts/compatibility/Run-Delivery.ps1` 复现六组独立版本/架构的构建、.NET/JS、SmokeHost/CDP、普通/私密 OOPIF、三版 x64 WPF、NuGet 内容、公开包消费者及固定未改写语料验证。

公开消费者验证 350003 字节暂存上传、跨块 UTF-8 的增量前缀、回调字段及真实 iframe target 内的 prepared GM。原生 OOPIF 门禁另外验证双脚本隔离、嵌套、进程迁移、拒绝请求、私密 jar 隔离和释放平衡。这些检查不能替代固定 TM 的原生菜单输入或缺少的下载/通知交互证据。

此前 `artifacts/p2-delivery/086908df9daa` 的六组合通过了当时断言，但 84/x86 普通 OOPIF 记录仍有 CEF frame 被释放引起的 MSC101，现已被最终验收结果替代。`43ffd49` 先保护位置求值，但收紧后的下一轮矩阵在 84/x86 停止：源码派发仍有第二个释放窗口，完整异常堆栈定位到原生 IsValid getter。`1ef4c4f` 改为先检查释放状态，并取消派发前/派发期间确认失效的操作，有效 frame 的异常继续正常传播。两个回归场景在各自修复前均失败，修正后的 Adapter 68 项测试通过。原生门禁拒绝未解释的 MSC101/MSC231/MSC233/MSC236 初始化和派发错误，最终矩阵使用两项修复和该门禁，历史失败保留在证据中。

先前 PackageOnly pilot 的 browser-wide CanExecute 轮询竞态与修正继续保留在[pilot 证据](../../scripts/compatibility/evidence/enhanced-consumer-pilot-p1.json)。已有固定 stable/beta fixture 和参考结果继续引用，不改称本轮新跑的差分。历史失败、CSP 限制、首 inline 时机和 Chromium 84 replaceAll 限制均保留，expected-engine-limitation 不计为功能 passed。

## 仍待完成

完整 M1 仍需固定参考的保存文件/ZIP/取消权限证据、参考语料 history 刷新失败的处理，以及下载/通知原生交互对照。完整第 30 步仍需缺少的原生 frame 路由和 beta 输入对照。显式 Cookie 合并、请求分区、audible、历史 MV2 等价、严格 start 及透明 BFCache 各自维持未支持或待验证状态。后续实施应先明确可用原生路线或选择具体专项，本轮没有隐式引入 renderer subprocess 或替换网络后端。
