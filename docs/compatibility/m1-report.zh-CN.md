# 稳定版后续改进交付评估

[English / 37 项 API 核对表](m1-report.md)

本页是第 21～27 步实现快照，后续增强及当前原生/包消费验证见[增强范围交付评估](m2-report.zh-CN.md)。后续功能进展不消除下述 M1 参考证据缺口；本页旧能力限制应结合新报告阅读。

本轮交付属于**限定范围基础交付**。完整 M1 和原第 15 步仍为**未完成**：固定参考浏览器未授予下载权限，真实语料的页面 history wrapper 虽已安装但资源刷新断言失败，TM 通知点击/默认动作/导航和下载权限/保存失败等交互尚未补齐。不能用原生测试数量换算兼容率，也不降低原计划标准。

本轮最终被验证实现为 `fe3c8df`（第 21～26 步、WPF 轮询及原生测试宿主修复）。[最终矩阵证据](../../scripts/compatibility/evidence/stable-follow-up-delivery-p0.json)保存真实 commit/patch、版本/架构、包消费、源码语料、WPF 及此前失败；历史 p2-delivery 结果继续保留原日期和来源。

## 最终原生/包消费矩阵（2026-11-11）

| CefSharp | 架构 | .NET | JS | SmokeHost/CDP | WPF | 源码/包消费语料 | NuGet/公开消费者 |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 84.4.10 | x64 | 413 通过 | 120 通过 | 通过 | 通过 | 10 + 10，声明范围 | 通过 |
| 84.4.10 | x86 | 364 通过 | 120 通过 | 通过 | 不适用 | 10 + 10，声明范围 | 通过 |
| 121.3.70 | x64 | 413 通过 | 120 通过 | 通过 | 通过 | 10 + 10，声明范围 | 通过 |
| 121.3.70 | x86 | 364 通过 | 120 通过 | 通过 | 不适用 | 10 + 10，声明范围 | 通过 |
| 151.3.240 | x64 | 413 通过 | 120 通过 | 通过 | 通过 | 10 + 10，声明范围 | 通过 |
| 151.3.240 | x86 | 364 通过 | 120 通过 | 通过 | 不适用 | 10 + 10，声明范围 | 通过 |

最终矩阵没有未解释失败。84 每次语料运行是 6 项通过、4 项明确引擎限制（CSP、首 inline 时机、两项设置界面）；121/151 是 8 项通过、2 项 CSP/时机限制。源码与包消费者共 120 次场景执行均保留逐项结果，不把限制计为功能 passed。补丁只有报告文档变化，运行时、runner 与语料输入均已提交。原始结果位于 `artifacts/p2-delivery/e9c8dbd16171`。

首轮 84/121 双架构四组完整通过；151 WPF 在禁用触发 reload 时取消求值，独立复核再次失败。最小 canceled→false→true 轮询用例在修复前失败；修复后仅在原 25 秒期限内重试 TaskCanceledException，保留直接求值、断言及编程错误，持续取消仍超时。49 项 Demo 测试及原 151 WPF 流程通过；两个原始失败均保存在[轮询证据](../../scripts/compatibility/evidence/wpf-polling-follow-up-p0.json)，最终六组合对修复后的提交重新运行。

151/x86 语料还曾出现无报告超时。测试宿主现先等待 Cookie 存储原生完成回调、释放临时 manager，限定求值时限，并保存阶段及进程退出信息。此后 8 次独立探针和 151/x86 十场景完整运行通过声明范围；此前超时及一次本地诊断构建与探针重叠导致的文件锁保存在[就绪证据](../../scripts/compatibility/evidence/corpus-host-readiness-p0.json)。这些修正不等于证明了全部底层原生超时原因。

## 实际修改

| 步骤 | 已交付范围 | 保留边界 |
| --- | --- | --- |
| 21 | 稳定参考身份/源码 hash 固定，独立 profile、失败 fixtures 和证据 | 下载权限交互、通知原生点击、beta/MV2 未取得项不记 passed |
| 22 | DOM 创建/属性/插入错误返回 null，modern 完成为 null；head/body/documentElement 回退 | 参数/编程错误仍可见；创建节点不证明 CSP 效果 |
| 23 | 三版真实服务器确立 URLRequest 忽略显式 Cookie，默认后端发送前拒绝 | 没有实现 TM 显式 Cookie 合并；真实 jar 登录、匿名、重定向、上下文隔离保留 |
| 24 | once/signal/capture、重复身份、派发增删、异常隔离、URL 去重及结束清理 | 保留普通对象；oldURL/属性优先/门面 receiver/signal 是额外保证 |
| 25 | 权限/不支持/过期下载错误分类；真实细分能力沿用 compatibility map；中英文清单同步 | 不添加 Demo 扩展名白名单；下载成功仍等文件提交 |
| 26 | 两个 MIT 固定未改写源码、七类独立场景及 CSP/cancel/拒绝变体；源码与 NuGet consumer 共用公开 API 测试宿主 | 完整固定参考差分仍未达标 |
| 27 | 六组合原生/打包/消费、三版 WPF 的交付门禁及本报告 | 完整 M1 取决于缺失参考证据，不由 native 通过替代 |

## 验收口径

[37 项功能核对表](m1-report.md#api-review-37-functional-rows)逐项列出实际范围：legacy/modern 的立即返回与 Promise、宿主服务、权限、世界和版本条件继承[契约表](api-contract.md)，不按 API 名称数量宣布完成。

稳定范围仍存在以下公开限制：strict full-GM document-start 仅 best effort，OOPIF 和透明 BFCache 恢复未支持，isolated 子 frame 原生手动菜单未接通，clickedFrame/完整原生菜单事件未提供；请求 cookiePartition 和显式 Cookie 被默认网络后端拒绝；分区 CRUD 仅在 121/151 的已声明身份范围成立；audible 没有可靠来源。这些事实通过已有 GM.info.compatibility.capabilities 报告，未知自定义后端事实不伪造。

## 七类语料与来源

SiteAssetDL 1.0.0 覆盖 DOM、页面 hook、登录 XHR、下载、iframe/SPA；Find Scripts For This Site 0.4.3 覆盖存储监听与菜单配置。每类单独断言，源码/许可证 SHA-256、不可变 URL、完整 metadata、世界、前置页面和假 Cookie 均写入[清单](../../scripts/compatibility/corpus.json)。没有 require/resource 隐藏输入，没有修改 match/connect、插入 polyfill 或删失败分支。

真实副作用包括服务端方法/认证 Cookie/跨 origin 端口重定向、保存 PNG/ZIP 内条目的字节 hash、双实例设置菜单变化和导航后持久化；拒绝场景在阶段标记之后检查无 GM 网络请求。84 原样设置界面触发 replaceAll TypeError，121/151 提供该语言能力；只将确切实测失败归类为引擎限制。同进程 iframe/SPA 不代表 OOPIF/BFCache。页面 hook 首 inline 时机和启动后实际效果分别记录；CSP 通过真实响应头测试，不以节点存在假称样式成功。

固定 TM 的[实际记录](../../scripts/compatibility/evidence/corpus-reference-stable-p0.json)保留资源刷新失败与保存/ZIP/cancel blocked。三版 native 和包消费结果只能证明各自声明范围，不能补写 TM 参考结果。初次 WPF 84 原生回调崩溃及同构建重试、delivery 补丁换行转换遭 hash 拒绝等初次失败也保留。源码/许可证按 binary 固定，补丁先应用 attributes，避免改变上游字节。

完整 M1 尚需补齐参考权限/保存/通知交互以及同场景语料证据。历史 MV2、beta 新 API、audible、请求分区不作为新增前置，但继续公开未支持/待验证范围。参见[复现命令](validation.md)及[步骤证据](p0-follow-up.md)。
