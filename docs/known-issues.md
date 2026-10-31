# Known issues and technical debt

This list reflects the alpha.19 implementation. A topic is a project, and a tool/session pair is a routing claim, not an authenticated identity.

## Notification reliability

### Long-idle Claude Desktop renewal is not yet verified on the host

The plugin requests a renewal turn after 12 idle hours and depends on Claude Desktop accepting the long hook timeout, delivering the turn, and running the Stop hook to arm a replacement. Short-deadline integration tests cover Pigeon's side, but a live Desktop renewal and the 12-hour timeout have not been verified. Each renewal uses the session's model; an otherwise idle session can use up to two renewal turns per day. Test a short renewal in an isolated Desktop conversation, then verify the long timeout before depending on unattended delivery.

If the service is unavailable when renewal is due, a waiter that owned the session when it last reached the service still requests one renewal, so the next Stop hook re-arms a waiter that waits for the service to return. If another notifier owns the session, a standby hook can still expire; if that notifier later disappears, no hook waiter remains until another turn. Messages remain queued, subject to their normal expiry and retry limits.

### Waiter lifetime follows its launcher chain, within limits

A hook waiter or monitor holds every shell between itself and the first non-shell ancestor (normally Claude Code), plus that ancestor, and stops when any of them exits; an isolated test confirms that killing only the outer Git Bash process stops the waiter before it claims a wake. Limits: an ancestor that cannot be opened, has exited, or started after its child (a reused process ID) ends the chain unwatched; a non-shell process between the waiter and the host also ends the chain, so a host above it is not watched; and the walk stops after eight levels. Wake retries and manual retrieval still recover queued messages if a waiter is lost.

### Desktop reopen and app restart depend on the host

Pigeon can restore an existing session's association when Claude Desktop starts its process. Tests observed both automatic process startup on reopen and conversations that did not start until a user message. A hook cannot wake a process that does not exist. Test supported Desktop startup behavior and report the session as unavailable rather than claiming guaranteed wake after every reopen.

### Codex Desktop recovery needs a host test

The Codex plugin's startup and prompt hooks use the host-provided session ID, but the actual Desktop hook trust flow, server name and automatic binding on resume remain unverified. Test with a new isolated Codex conversation and verify queued delivery, busy behavior and restart separately.

### Delayed Codex wake prompts can be repeated

Codex may present wake prompts that have already been confirmed and completed. The generation check rejects those stale prompts, but repeated prompts are distracting. Investigate whether the supported Codex queue interface allows safer coalescing or cancellation, and retain generation checks and bounded retries.

## Other technical debt and operating limits

- Migration from project namespaces can refuse records Pigeon created itself (a `__pigeon__` sender in several old projects) or fail with a raw SQLite error when two old agents hold routes for the same tool and session. This depends on data from older versions and is untested.
- Legacy non-topic mode is kept only for tests: production always enables topic mode, but `Mailbox` still carries both code paths.

- Claude notifier ownership is held in service memory and expires after 15 seconds. After a service restart, competing notifiers reacquire it in whatever order they reconnect; integration tests cover a monitor and a hook waiter racing after a restart and a monitor that dies without releasing.
- The Claude hook wrapper uses Git Bash. Its fallback for older CLI versions suppresses errors to avoid false wakes. `pigeon status` reports the service and CLI versions and flags a Claude session that has had no notifier since the service started, which is how an incompatible or missing CLI shows up. The PowerShell alternative is not packaged because the current environment blocks unsigned scripts.
- The loopback service has no application-level authentication. Any local process able to connect can impersonate a session or access messages. Topic routing is not an access-control boundary; authentication was explicitly deferred for the MVP.
- Messages and wakes use bounded retries and may be delivered more than once. Transport acceptance, runtime receipt and agent acknowledgment are separate events. Keep explicit acknowledgment and idempotency; do not claim exactly-once delivery.
- Peer messages are untrusted data, do not grant permissions, and do not reserve files. Multiple agents can still edit the same files concurrently.
