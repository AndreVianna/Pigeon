# Known issues and technical debt

This list reflects 0.6.0-alpha.4. A topic is a private conversation between two sessions, and a session ID is a routing claim, not an authenticated identity.

## Other machines

### Tried across two machines with the CLI only

0.6.0-alpha.4 is tested with two services on one machine over real TLS on loopback. The alpha.3 version (invitations and per-topic sharing) was tried on 2026-11-04 between two Windows machines on a home Wi-Fi network; the join codes that replaced it, agents on two machines, macOS, Linux and Tailscale are not yet tried.

### Accepted limits of the join handshake

See "Accepted limits" in the [remote connections design](remote-design.md): on first contact only the listing side checks the code, so an active attacker on your network who takes the listing machine's address could pose as it; both machines need an inbound port, which managed corporate devices and isolated guest Wi-Fi often block; an agent with a shell can run `pigeon allow` and `pigeon requests --show`; a host can answer prompts without a person (headless runs decline them, a Claude Code hook could answer them); and request spam is limited only by one request per topic, a rate limit and Deny.

### Antigravity is local only

Antigravity advertises MCP elicitation but cancels every prompt, so its sessions cannot open or join topics with other machines, or require approval on local topics.

### The local API is still unauthenticated

Another user account on a machine can use that machine's Pigeon service, including its topics with other machines and the commands that answer join requests. Use topics with other machines only on single-user machines. Authenticating the local API is planned for later.

### Addresses are fixed

There is no discovery: the first join names the other machine's address or host name. A known machine's address is updated whenever it makes contact; if both machines change address at once, set one with `pigeon peer address`. Both machines need their peer listener open for messages to flow both ways.

## Claude Desktop

### Join prompts are answered in a terminal

Claude sessions in the Claude desktop app cancel Pigeon's prompts without showing them (found on 2026-11-07), so Pigeon does not send them any. A join request, a contact confirmation or a join code for such a session is answered with `pigeon requests`, `pigeon allow` and `pigeon deny` in a terminal; the agent says so.

## Antigravity

### Tried only with a development copy

The 0.6.0-alpha.2 host test (A1–A8 in `experiments/host-test`) ran a development copy of the plugin installed in a test workspace, with the sidecar installed on its own. Installing the production plugin globally (`agy plugin install`, with the plugin's own **Pigeon wake** sidecar) has not been tried. Antigravity CLI conversations have not been tried.

### Waking needs the App and the sidecar

Only the Antigravity App runs sidecars; the Antigravity CLI and IDE get messaging and restore but are not woken. The **Pigeon wake** sidecar is off until the user turns it on in Automations. Antigravity asks for approval once per tool, not once per server: allowing `pigeon_join` does not allow `pigeon_receive`, so the first wake waits until each tool it uses is allowed (seen in the 0.6.0-alpha.2 host test).

### Turn state is kept in memory

The service learns whether a conversation is idle only from its hooks. After the service restarts, a conversation is not woken until its next turn reports. A conversation whose turn ended abnormally without a `Stop` hook stays marked busy until its next turn.

### Acceptance is not delivery

`agentapi send-message` reports success for a deleted conversation and drops the message. Pigeon's wake retries cover this: an unconfirmed wake is resent after the retry time and gives up after the attempt limit.

## OpenCode

### Tried only with a development copy

The 0.6.0-alpha.2 host test (O1–O6 in `experiments/host-test`, OpenCode 2.2.5 on Windows) ran a development copy of the plugin installed in one test project. Installing the production plugin in `~/.config/opencode/plugins` has not been tried, and neither has macOS or Linux.

### OpenCode 2 only

OpenCode 1.x plugins use a different API, and OpenCode 2 does not run them. The Pigeon plugin is for OpenCode 2; OpenCode 1.x is not supported.

### Waking needs OpenCode's server

The plugin runs inside OpenCode's shared background server, which the CLI and Desktop start. In the host test that server kept running after OpenCode Desktop was quit and woke a session with no window or terminal open; a reopened session then shows what it did meanwhile. While no OpenCode server runs, nothing wakes an OpenCode session. A session that has not reported its status since OpenCode started is treated as idle; its wake is queued, so it cannot interrupt a turn that is in fact running.

## GitHub Copilot

### Tried only with a development copy

The 0.6.0-alpha.2 host test (C1–C6 in `experiments/host-test`) ran a development copy of the plugin, installed with `copilot plugin install` from a local path, in VS Code 1.145 and the GitHub Copilot app on Windows. Copilot CLI has not been tried. The CLI warns that installs from a local path are deprecated in favour of `plugin@marketplace`, so a release should publish the plugin through a marketplace.

### Local chats in VS Code are not woken

VS Code's older Local chats run outside the Copilot runtime: Pigeon identifies them from the chat ID VS Code puts in each call, but nothing can wake them.

### Copilot extensions are new

The wake depends on Copilot extensions, which the documentation calls experimental. The runtime also outlives a VS Code restart, so a changed plugin may need the runtime (`copilot-runtime.exe`) to end before it reloads.

## macOS and Linux

### Not yet tried with Claude or Codex

0.6.0-alpha.1 runs on macOS and Linux and passes the automated tests there (CI on Ubuntu and macOS), including the hook waiter's parent watching and the hook wrapper under dash. It has not yet run with Claude Code or Codex on either system. In particular, desktop apps started from the macOS Dock or a Linux launcher may not see the `PATH` set in a shell profile, so they may not find `pigeon` in `~/.dotnet/tools`.

### Background service

`pigeon start` starts the service detached from the terminal and the service ignores the terminal's hang-up signal, so it survives closing the terminal. It is not a launchd agent or systemd unit, so like on Windows it does not restart after a reboot or log-out.

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
- On Windows the Claude hook wrapper uses Git Bash; on macOS and Linux it runs under the system `sh` and uses only POSIX features. Its fallback for older CLI versions suppresses errors to avoid false wakes. `pigeon status` reports the service and CLI versions and flags a Claude session that has had no notifier since the service started, which is how an incompatible or missing CLI shows up. The PowerShell alternative is not packaged because the current environment blocks unsigned scripts.
- The loopback service has no application-level authentication. Any local process able to connect can impersonate a session or access messages. Topic routing is not an access-control boundary; authentication was explicitly deferred for the MVP.
- Transport acceptance, runtime receipt and acknowledgment are separate events; do not claim exactly-once delivery.
- Peer messages are untrusted data, do not grant permissions, and do not reserve files. Multiple agents can still edit the same files concurrently.
