# Known issues and technical debt

This list reflects the alpha.19 implementation. A topic is a project, and a tool/session pair is a routing claim, not an authenticated identity.

## Notification reliability

### Long-idle Claude Desktop renewal is not yet verified on the host

The plugin requests a renewal turn after 12 idle hours and depends on Claude Desktop accepting the long hook timeout, delivering the turn, and running the Stop hook to arm a replacement. Short-deadline integration tests cover Pigeon's side, but a live Desktop renewal and the 12-hour timeout have not been verified. Each renewal uses the session's model; an otherwise idle session can use up to two renewal turns per day. Test a short renewal in an isolated Desktop conversation, then verify the long timeout before depending on unattended delivery.

If the service is unavailable when renewal is due, the owner cannot be confirmed and no renewal is emitted. If another notifier owns the session, a standby hook can expire; if that notifier later disappears, no hook waiter remains until another turn. Messages remain queued, subject to their normal expiry and retry limits. Improve recovery without generating unbounded model turns or multiple competing wake attempts.

### A hook waiter can outlive an outer shell

An isolated test killed the outer Git Bash process without killing its whole process tree. An intermediate shell and waiter survived, and the waiter claimed a wake whose output had no listener. The current process-lifetime check watches only the immediate parent. Track the relevant host ancestry safely, test parent death and PID reuse with isolated processes, and verify that no detached waiter can consume attempts. Until fixed, wake retries and manual retrieval can recover queued messages, but timely notification is not guaranteed.

### Desktop reopen and app restart depend on the host

Pigeon can restore an existing session's association when Claude Desktop starts its process. Tests observed both automatic process startup on reopen and conversations that did not start until a user message. A hook cannot wake a process that does not exist. Test supported Desktop startup behavior and report the session as unavailable rather than claiming guaranteed wake after every reopen.

### Codex Desktop recovery needs a host test

The Codex plugin's startup and prompt hooks use the host-provided session ID, but the actual Desktop hook trust flow, server name and automatic binding on resume remain unverified. Test with a new isolated Codex conversation and verify queued delivery, busy behavior and restart separately.

### Delayed Codex wake prompts can be repeated

Codex may present wake prompts that have already been confirmed and completed. The generation check rejects those stale prompts, but repeated prompts are distracting. Investigate whether the supported Codex queue interface allows safer coalescing or cancellation, and retain generation checks and bounded retries.

## Other technical debt and operating limits

- Migration from project namespaces can refuse records Pigeon created itself (a `__pigeon__` sender in several old projects) or fail with a raw SQLite error when two old agents hold routes for the same tool and session. This depends on data from older versions and is untested.
- Legacy non-topic mode is kept only for tests: production always enables topic mode, but `Mailbox` still carries both code paths.

- Claude notifier ownership is held in service memory and expires after 15 seconds. After a service restart, competing notifiers reacquire it; test multi-notifier races and stale-owner recovery more thoroughly.
- The Claude hook wrapper uses Git Bash. Its fallback for older CLI versions suppresses errors to avoid false wakes; status and diagnostics should make an incompatible or missing CLI easier to identify. The PowerShell alternative is not packaged because the current environment blocks unsigned scripts.
- The loopback service has no application-level authentication. Any local process able to connect can impersonate a session or access messages. Topic routing is not an access-control boundary; authentication was explicitly deferred for the MVP.
- Messages and wakes use bounded retries and may be delivered more than once. Transport acceptance, runtime receipt and agent acknowledgment are separate events. Keep explicit acknowledgment and idempotency; do not claim exactly-once delivery.
- Peer messages are untrusted data, do not grant permissions, and do not reserve files. Multiple agents can still edit the same files concurrently.
