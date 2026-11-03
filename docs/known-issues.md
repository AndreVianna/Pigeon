# Known issues and technical debt

This list reflects the 0.5.0 release. A topic is a private conversation between two sessions, and a session ID is a routing claim, not an authenticated identity.

## Notification reliability

### Long-idle Claude Desktop renewal

The plugin requests a renewal turn after 12 idle hours and depends on Claude Desktop accepting the long hook timeout, delivering the turn, and running the Stop hook to arm a replacement. The short (3-minute) renewal passed host tests H2 and E3, and the user reported the 12-hour run (H4) as passed on 2026-11-03. Each renewal uses the session's model; an otherwise idle session can use up to two renewal turns per day.

If the service is unavailable when renewal is due, a waiter that owned the session when it last reached the service still requests one renewal, so the next Stop hook re-arms a waiter that waits for the service to return. Messages remain queued, subject to their normal expiry and retry limits.

### Waiter lifetime follows its launcher chain, within limits

A hook waiter holds every shell between itself and the first non-shell ancestor (normally Claude Code), plus that ancestor, and stops when any of them exits; an isolated test confirms that killing only the outer Git Bash process stops the waiter before it claims a wake. Limits: an ancestor that cannot be opened, has exited, or started after its child (a reused process ID) ends the chain unwatched; a non-shell process between the waiter and the host also ends the chain, so a host above it is not watched; and the walk stops after eight levels. On the host (Claude Desktop with Claude Code 2.1.309), the chain depends on how the hook is started. With the 0.4 plugin's direct `hook-wait.sh` command, the launcher above the hook shells exited at once and the chain ended at the top shell, so Claude Code itself was not watched. Since 0.5.0-beta.2 the hook runs as `sh "…/hook-wait.sh"`, and on 2026-11-03 the chain reached `claude.exe` (`pigeon <- sh <- sh <- sh <- bash <- bash <- claude.exe`); killing the outer shell still stops the waiter within about three seconds. Wake retries and manual retrieval still recover queued messages if a waiter is lost.

### Desktop reopen and app restart depend on the host

Pigeon can restore an existing session's association when Claude Desktop starts its process. Tests observed both automatic process startup on reopen and conversations that did not start until a user message. A hook cannot wake a process that does not exist. The user reported the reopen and restart behavior (H6) as observed on 2026-11-03. Report a session as unavailable rather than claiming a guaranteed wake after every reopen.

### Codex Desktop recovery

The Codex plugin's startup and prompt hooks use the host-provided session ID to restore a joined session. The user reported Codex recovery (H5: idle, busy, close and reopen, app restart) as passed on 2026-11-03, and a Codex chat was observed restoring its session after an app restart. A chat's MCP server that exits (for example when the Pigeon service is stopped for an update) is not restarted by Codex; that chat needs to be reopened or the app restarted.

### Delayed Codex wake prompts can be repeated

Codex may present wake prompts that have already been confirmed and completed. The generation check rejects those stale prompts, but repeated prompts are distracting. Investigate whether the supported Codex queue interface allows safer coalescing or cancellation, and retain generation checks and bounded retries.

## Other technical debt and operating limits

- Upgrading an earlier database keeps only sessions that had a tool/session route and at most two members per topic; name-based agents and their messages are not carried over. The upgrade is tested with a synthetic database, not with a real one from an earlier version.
- A topic is deleted when both members have been disconnected for an hour of service running time. A conversation closed for longer than that loses its topic and any messages still waiting in it; it has to join again.
- Messages and wakes use bounded retries and may be delivered more than once: if an agent fails before completing a wake, all messages received under it are delivered again.

- Claude notifier ownership is held in service memory and expires after 15 seconds. After a service restart, the hook waiter reacquires it when it reconnects.
- The Claude hook wrapper uses Git Bash. Its fallback for older CLI versions suppresses errors to avoid false wakes. `pigeon status` reports the service and CLI versions and flags a Claude session that has had no notifier since the service started, which is how an incompatible or missing CLI shows up. The PowerShell alternative is not packaged because the current environment blocks unsigned scripts.
- The loopback service has no application-level authentication. Any local process able to connect can impersonate a session or access messages. Topic routing is not an access-control boundary; authentication was explicitly deferred for the MVP.
- Transport acceptance, runtime receipt and acknowledgment are separate events; do not claim exactly-once delivery.
- Peer messages are untrusted data, do not grant permissions, and do not reserve files. Multiple agents can still edit the same files concurrently.
