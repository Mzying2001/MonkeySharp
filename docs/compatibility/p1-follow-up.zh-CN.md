# P1 增强兼容性实施进度

本页跟踪第 28～33 步及第 36 步交付；[完整 M1 缺口](m1-report.zh-CN.md)继续保留。实验评估完成不等于对应能力已实现。

| 步骤 | 进度 |
| --- | --- |
| 28 | 非 fetch 文本/JSON/document/xml 在网络完成前提供累积 responseText；文本 response 同步可见。单路 64 KiB 拉取、同一增量解码器、50 ms 回调节流、offset 校验及单一终态；[证据](../../scripts/compatibility/evidence/progress-follow-up-p1.json)。 |
| 29 | 授权后有界消费流、暂存后上传、取消/导航清理、暂存重放；[证据](../../scripts/compatibility/evidence/stream-body-follow-up-p1.json)。 |
| 30～33 | 待实施。 |
| 36 | 待最终矩阵。 |

JSON/document 保留最终解析时机，二进制/stream 不暴露部分文本。保留现有 fetch 不报告进度/中间 readyState 的边界；固定 5.6.6242 实际报告 fetch 进度，这一有意差异不算对齐。无效字节使用替换字符，未知字符集沿用 UTF-8 回退。

固定 beta 包通过 CRX3 发布者签名与包 hash 验证，解包时仅从匹配 signed identity 的公钥恢复 manifest.key，执行源码未改写。初轮错误选择商店签名公钥且错误假设 fetch 没有进度，失败记录保留且不作为身份合格证据；修正后的发布者身份及可复现解包结果分别保存。复现命令见[英文进度页](p1-follow-up.md)。

固定 beta 在受控 HTTP/2 TLS 场景上传字节正确；HTTP/1.1 的 Failed to fetch 保留。beta 保留 reader 锁，307 因 disturbed stream 拒绝，disturbed/producer 异常在 1.5 秒观察窗未终结；不推断其永久不终结。MonkeySharp 主动释放 reader、及时拒绝坏输入并重放暂存字节。拒绝授权不获取 reader；producer 自行预填充其 stream 不属于本 API 消费。
