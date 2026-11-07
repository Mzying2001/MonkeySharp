# Notifications, menus and clipboard

Both calling forms share operation implementations while preserving their immediate returns. The [API inventory](api-contract.md) and [userscript guide](../usage-guide.md#menus-notifications-and-clipboard) / [中文](../usage-guide.zh-CN.md#menus-notifications-and-clipboard) describe parameters and examples. Host interfaces are covered by the [integration reference](../integration-reference.md#host-service-contracts).

## Notifications

Handles are created without effects, subscribed, then started. Client IDs exist before bridge calls. Modern completion waits for closure and resolves true for a clicked notification or false otherwise; legacy returns undefined. Start failures reject. Click callbacks receive a cancelable Event; default URL actions wait for acknowledgment, with a five-second fallback. Callback exceptions are reported independently.

Tags replace notifications belonging to the same installation in one host, not global OS notifications. Document end closes plain notifications and mutes callbacks on retained url/tag notifications. Host disposal closes all notifications. System-toast appearance and cross-host tag replacement are not claimed.

## Menus

Registration/removal share normalization and ordered submission. Numeric/string IDs update an execution's registration. Legacy registration returns an ID and modern registration resolves one; legacy removal returns undefined and modern removal resolves undefined. WPF merges same-installation name/title/access-key entries while retaining frame subscriptions, tooltips and autoClose behavior.

The renderer creates a MouseEvent; native coordinates/modifiers and trusted browser activation are not transported. Callback failures do not prevent other subscribers. The later `clickedFrame` option is explicitly rejected. Frame navigation and unregister remove only their owned subscriptions.

## Clipboard

Text/HTML shorthand, MIME strings and info objects normalize into validated text/plain or text/html requests. WPF uses STA dispatch and contention retries; CF_HTML offsets count UTF-8 bytes. Callback and Promise completion follow the actual write. Unsupported formats fail before mutation.

## Evidence and reproduction

The [pinned return record](../../scripts/compatibility/evidence/tampermonkey-5.5.1-returns.json) establishes modern return shapes and notification timeout completion. The [native/WPF interaction matrix](../../scripts/compatibility/evidence/interactions-p0.json) verifies click/default prevention, tag replacement, highlight-only completion, merged frame menus, per-frame cleanup and actual Unicode/CF_HTML bytes. Interactive WPF checks and native effects remain distinct from the headless reference's observations.

SmokeHost notifications are lifecycle test doubles; WPF is the actual interaction gate. Its clipboard fixture restores the original clipboard in a finally block. Reproduction commands are in [validation](validation.md).
