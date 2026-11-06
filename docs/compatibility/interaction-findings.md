# P0 host interactions

The implementation follows the pinned 5.5.1 return evidence and the official
[notification](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_notification),
[menu](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_registerMenuCommand)
and [clipboard](https://www.tampermonkey.net/documentation.php?locale=en&q=GM_setClipboard)
contracts. The later `clickedFrame` option is explicitly rejected.

Notification handles are created without effects, subscribed, then started.
Client IDs exist before the bridge call. Modern completion waits for closure,
with true for a clicked notification and false for other completion. Click
callbacks receive a cancelable Event; the host's default URL action waits for
the script acknowledgment (five-second fallback). Exceptions in script callbacks
are reported independently. Start failures reject rather than appearing as a
successful dismissal. Tags replace the same installation's notification in one
host; they are not global OS tags. Document end closes plain notifications and
mutes callbacks of retained url/tag notifications. Host disposal closes all.

Menu registration and removal share normalization and ordered submission across
the two facades. Numeric/string IDs update an execution's registration. Removal
returns undefined in both styles. WPF merges same-installation name/title/access
key entries, retains separate frame subscriptions and applies tooltip/autoClose.
The renderer creates a click MouseEvent; native coordinates/modifiers are not
transported. Callback failures do not prevent other subscribers from running.

Clipboard text/HTML shorthand, MIME strings and info objects normalize into
validated text/plain or text/html requests. STA dispatch and contention retries
remain. CF_HTML offsets count UTF-8 bytes; callback and Promise completion follow
the successful write. Other formats fail before clipboard mutation.

Validation on 2026-11-06: 244 Core, 37 adapter and 38 Demo tests; 85 JavaScript
tests. The 84/x64 complete baseline, six real SmokeHost/CDP combinations
(84/121/151, x64/x86), and 84/121 x64 WPF E2E passed. WPF verifies waiting before
interaction, tag replacement, URL default action/prevention, highlight-only
completion, merged-frame callbacks, ID update, per-frame unregister/navigation
cleanup, and actual Unicode/CF_HTML clipboard bytes. The original clipboard is
restored in a finally block. The SmokeHost notification implementation remains
a lifecycle test double; WPF evidence is the actual interaction gate.

Raw logs: `artifacts/p0-step5-*`. Committed WPF observations:
`evidence/interactions-p0.json`. No system-toast appearance, native mouse details,
or global cross-host tag replacement is claimed.
