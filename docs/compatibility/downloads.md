# Downloads

Both calling forms share one client-identified operation. `GM.download` immediately returns its original abortable completion Promise; `GM_download` returns an abort-only handle. The host registers cancellation before awaiting operation creation, subscribes before Start and delivers one terminal result. Start acknowledgment cannot complete the public Promise. Callback exceptions are reported independently.

`FileDownloadService` transfers through the `IHttpRequestService` supplied by its host. When composed through the CefSharp builder's download factory with the default HTTP service, as in the Demo, it shares CEF URLRequest/RequestContext credentials and webRequest composition with XHR. A custom HTTP service determines its own network behavior. Bounded chunks are written to a temporary file. Success follows flush, close and atomic move/replace; failure or cancellation removes staging files before terminal notification. Completion details are `{url, name, loaded}`, without local paths or internal IDs.

Accepted tasks continue across document end, navigation, script disable and uninstall under captured authorization; redirect targets still need approval. Document callbacks are muted after execution ends. Host disposal cancels tasks, and the Demo drains them before releasing its browser context. This ownership policy is distinct from JavaScript callback delivery.

## Error causes

Permission/grant denial maps to `not_permitted`, unsupported options/backend to `not_supported`, I/O/cancellation/expired execution to `not_succeeded`, and timeout to `timeout`. Callback delivery precedes Promise rejection and only one terminal result is delivered. Structured host errors retain their cause/details. `not_whitelisted` is reserved for an actual filename-extension policy; the Demo does not introduce one. `not_enabled` describes a disabled download feature, not session expiry. Native save cancellation and disk failure remain separately diagnosable through details. Reference permission/UI cases still marked blocked are not claimed as differential passes.

## Binary inputs and completion

Blob/File and blob URLs are resolved in their owning renderer. Acknowledged 64 KiB chunks stage binary downloads before handing the body to the accepted task. Incomplete staging is execution-scoped and released on failure/navigation. Custom download services read `DownloadRequest.Body` and retain it through termination rather than attempting HTTP access to a renderer blob identifier.

XHR streams have a different completion boundary: their Promise resolves when headers and the stream are available; later errors propagate through the stream. Downloads resolve only after file commit. Renderer pulls and host spooling bound bridge queues and disk use but cannot pause CEF's underlying network source. See [binary transport](../integration-reference.md#binary-transport-and-stream-completion).

## Evidence and reproduction

The [native download matrix](../../scripts/compatibility/evidence/downloads.json) records actual bytes and file hashes, authenticated redirects, anonymous denial, zero-byte files, immediate/transfer cancellation, timeout, disk failure, staging cleanup and completion after navigation. WPF checks exercise the actual save picker and cancellation; SmokeHost does not establish browser-extension download UI parity.

The [binary matrix](../../scripts/compatibility/evidence/binary-transport.json) includes native Blob/File/blob URL download cases. The [fixed stable binary-input reference](../../scripts/compatibility/evidence/tampermonkey-5.5.1-binary-inputs.json) probes XHR input and callback behavior; it does not establish saved-file download parity. See [XHR completion and evidence](network.md#completion-and-evidence) for its `context` observation.

Run the [baseline and native gates](validation.md). Script parameters and examples are in the [download guide](../usage-guide.md#downloads) / [中文](../usage-guide.zh-CN.md#downloads); host ownership and configuration are in the [integration reference](../integration-reference.md#host-service-contracts).
