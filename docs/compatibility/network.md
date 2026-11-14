# HTTP requests and streams

`GM_xmlhttpRequest` and `GM.xmlHttpRequest` share the adapter's CEF URLRequest transport and the requesting browser's actual RequestContext. The legacy form returns an abort handle; the modern form returns its original abortable Promise. The [userscript guide](../usage-guide.md#http-requests) / [中文](../usage-guide.zh-CN.md#http-请求) owns parameters and examples; the [integration reference](../integration-reference.md#binary-transport-and-stream-completion) / [中文](../integration-reference.zh-CN.md#二进制传输与流完成) owns host transport and budgets.

## Credentials and authorization

Ordinary authenticated requests use the actual browser Cookie jar; anonymous requests suppress it. Initial and redirected targets remain subject to script declarations, host policy and permission decisions. The default backend rejects explicit `cookie`, `headers.Cookie` and request `cookiePartition` before network dispatch. See [explicit Cookie observations](cookies.md#explicit-request-cookies) and [partition limits](cookies.md#partitioned-cookie-scope). Cookie CRUD support does not establish request-partition selection.

The HTTP service captures the attached RequestContext when it accepts a request and reuses it for every redirect, even if the host later attaches another browser. The application owns that context and must retain it until accepted downloads have drained; the service does not dispose it.

## Incremental response text

Non-fetch text, JSON, document and XML responses expose cumulative `responseText` during progress. Text also exposes cumulative `response`; JSON/document parsing remains final-only. Binary and stream responses expose no partial text. The renderer pulls at most 64 KiB at a time, uses a persistent decoder across chunk boundaries and bounds progress frequency without resending the complete prefix over the bridge. Malformed UTF-8 uses replacement characters; unsupported charset names retain the existing UTF-8 fallback.

MonkeySharp's fetch mode suppresses progress and intermediate readyState events. Fixed Tampermonkey 5.6.6242 emits fetch progress, so this is an intentional difference. The [incremental-text evidence](../../scripts/compatibility/evidence/progress-follow-up.json) links the identity-qualified [beta reference](../../scripts/compatibility/evidence/tampermonkey-5.6.6242-progress-repro.json); the [evidence index](../../scripts/compatibility/evidence/README.md#retained-failures-and-regressions) distinguishes earlier failed and superseded attempts.

## Stream request bodies

ReadableStream input is authorized before acquiring its reader, consumed one Uint8Array chunk at a time and staged in the existing bounded spool before CEF starts sending. Each bridge append is at most 64 KiB; unknown total lengths share the default 256 MiB body budget. The next read waits for the previous append acknowledgment. This is staged upload, with no live renderer-source network backpressure.

Locked/disturbed input, non-Uint8Array chunks and producer errors reject. Abort, navigation and staging failure release the reader and staging session. Redirects replay the completed spool without rereading the stream. Permission denial does not acquire a reader, although a producer can independently prefill its own stream before the API receives it.

Fixed beta uploaded the exact bytes over the controlled HTTP/2 TLS endpoint; HTTP/1.1 attempts failed. Its reader remained locked, 307 replay rejected a disturbed stream, and disturbed/producer-error operations had no terminal outcome within the 1.5-second observation window. These are bounded observations. MonkeySharp's reader release, prompt rejection and staged replay are deliberate guarantees. See [stream-input validation](../../scripts/compatibility/evidence/stream-body-follow-up.json) and the [fixed beta observation](../../scripts/compatibility/evidence/tampermonkey-5.6.6242-stream-body-observed.json).

## Completion and evidence

XHR `context` remains the original JavaScript value in MonkeySharp's callbacks and response. The [fixed stable binary-input reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-binary-inputs.json) observed a different object identity across Tampermonkey's extension boundary. This is an XHR difference; it supplies no evidence about saved-file download completion. See the [XHR response contract](../usage-guide.md#response-and-callbacks).

For stream responses, the modern Promise resolves when headers and the stream are available; later errors propagate through the stream. [Downloads](downloads.md) have a separate committed-file completion boundary. Host spooling and renderer pulls bound bridge queues and disk usage but cannot pause CEF's underlying response source.

The [native binary transport matrix](../../scripts/compatibility/evidence/binary-transport.json) covers binary/form inputs, redirects, cancellation and response types. The [fixed stable binary-input reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-binary-inputs.json) has narrower scope: Blob/File and blob-URL XHR inputs, returned bytes/text, abort-member exposure and callback context. It does not establish fixed-reference coverage for every native binary case. Incremental-text native checks add delayed chunks, split UTF-8, gzip and abort/timeout after partial delivery. Public package consumers check incremental text and exact staged upload bytes. See [recorded validation scope](validation.md#latest-recorded-validation) and [runner instructions](../../scripts/compatibility/README.md).
