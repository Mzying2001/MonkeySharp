# P1 增强兼容性实施进度

本页跟踪第 28～33 步及第 36 步交付；[完整 M1 缺口](m1-report.zh-CN.md)继续保留。实验评估完成不等于对应能力已实现。

| 步骤 | 进度 |
| --- | --- |
| 28 | 非 fetch 文本/JSON/document/xml 在网络完成前提供累积 responseText；文本 response 同步可见。单路 64 KiB 拉取、同一增量解码器、50 ms 回调节流、offset 校验及单一终态；[证据](../../scripts/compatibility/evidence/progress-follow-up-p1.json)。 |
| 29 | 授权后有界消费流、暂存后上传、取消/导航清理、暂存重放；[证据](../../scripts/compatibility/evidence/stream-body-follow-up-p1.json)。 |
| 30 | 已实现原生输入字段、菜单注册代际、可信 clickedFrame 过滤及 84 isolated 子 frame 手动菜单。121/151 子 frame 映射与 beta 原生点击/重叠一致性仍未完成；[证据](../../scripts/compatibility/evidence/menus-follow-up-p1.json)。 |
| 31 | 可行性评估完成；84/121/151 的对照及运行时往返均未真正命中缓存，恢复功能未实现；[证据](../../scripts/compatibility/evidence/bfcache-follow-up-p1.json)。 |
| 32～33 | 待实施。 |
| 36 | 待最终矩阵。 |

JSON/document 保留最终解析时机，二进制/stream 不暴露部分文本。保留现有 fetch 不报告进度/中间 readyState 的边界；固定 5.6.6242 实际报告 fetch 进度，这一有意差异不算对齐。无效字节使用替换字符，未知字符集沿用 UTF-8 回退。

固定 beta 包通过 CRX3 发布者签名与包 hash 验证，解包时仅从匹配 signed identity 的公钥恢复 manifest.key，执行源码未改写。初轮错误选择商店签名公钥且错误假设 fetch 没有进度，失败记录保留且不作为身份合格证据；修正后的发布者身份及可复现解包结果分别保存。复现命令见[英文进度页](p1-follow-up.md)。

固定 beta 在受控 HTTP/2 TLS 场景上传字节正确；HTTP/1.1 的 Failed to fetch 保留。beta 保留 reader 锁，307 因 disturbed stream 拒绝，disturbed/producer 异常在 1.5 秒观察窗未终结；不推断其永久不终结。MonkeySharp 主动释放 reader、及时拒绝坏输入并重放暂存字节。拒绝授权不获取 reader；producer 自行预填充其 stream 不属于本 API 消费。

第 30 步分开验收运行中的 GM 回调与 @run-at context-menu 用户源码启动。84 使用一次性 context 私有证明和打开菜单时捕获的文档代际，导航后不会误投新文档。121 的事件异步到达时临时 IFrame 已释放；151 的实测 isolated child 中没有 PostMessage；两版保留主 frame。未用页面 ID、URL 比较或 DOM 标记替代可信原生身份。完整第 30 步验收仍未完成。

固定 beta 菜单 fixture 需要 settings.headless=false 的参考 pin 和真实原生输入，再点击 Save menu evidence。computer-use 的 native pipe 在重试/重置后仍不可用，测试运行已停止，未发生原生选择。WPF 回调字段/桥接测试和 Core 过滤不算 beta 原生点击结果。

BFCache fixture 使用可缓存 HTTP A/B 页面，无 unload/beforeunload，通过原生后退、闭包 nonce 和 pageshow.persisted 判断。分别运行无 runtime/CDP 对照与 prepared raw/isolated GM；三版在默认和显式 enable-features=BackForwardCache 下仍为新闭包、persisted:false。这不证明所有 CEF 配置都不可能缓存，只是否决当前路线的恢复保证。因基本缓存命中前提未成立，私密上下文及暂停期间的监听/存储恢复继续未验证。复现命令见英文页；评估成功不等于能力支持。
