# Host tests: runbook

Live tests from [the release plan](../../docs/release-plan.md). They check a frozen build in the real hosts (Claude Desktop, Codex and, from 0.6.0-alpha.2, the Antigravity App) against an isolated service on port 49473 with a disposable database, using new test conversations and synthetic messages. Your normal service on port 49372 and your existing conversations are not used.

## 0.6.0-alpha.7 and alpha.8 dialog tests (D1–D7)

Prepared on 2026-11-07; not run yet. They check that hosts which cannot show Pigeon's prompts get Pigeon's own windows instead of the terminal: Claude Desktop on `dev-desktop` (192.168.2.20) and `office-laptop` (192.168.2.18), then Antigravity on dev-desktop. They also re-run M7 (removal) with alpha.6's fix. Real installs on both machines, new test conversations, topics named `dialog-test*`.

### Setup

1. **Both machines (you):** close Claude Desktop (and the Antigravity App on dev-desktop), then in the repository:
   `git pull`, `pigeon stop`,
   `dotnet pack src/Pigeon.Cli/Pigeon.Cli.csproj --output artifacts`,
   `dotnet tool update --global Pigeon.Cli --version 0.6.0-alpha.7 --source ./artifacts`,
   `pigeon start`. The Claude plugin reads from the repository folder, so `git pull` updates it.
2. **office-laptop:** `pigeon peer remove dev-desktop`, which clears the topic and message left over from M7 (alpha.6 cannot reach back to it). dev-desktop already knows no machines.
3. **Agents:** a new Claude Desktop conversation on each machine.

### Tests

| # | Test | Steps | Pass when |
|---|---|---|---|
| D1 | Open a topic | dev-desktop Claude: *"Open the Pigeon topic dialog-test to other machines. You may reply to Pigeon messages on that topic without asking me; replies only."* | `pigeon topics` shows it open to other machines. |
| D2 | The requester's windows | Laptop Claude: *"Join the Pigeon topic dialog-test@192.168.2.20. You may reply to Pigeon messages on that topic without asking me; replies only."* | A Pigeon window on the laptop asks to contact 192.168.2.20; after Allow a second window shows the join code with a Copy button. The agent says only that the request was submitted and does not mention a terminal. |
| D3 | The code window, and a wrong code | On dev-desktop a Pigeon window asks for the code. Enter a wrong one first, then the right one. | After the wrong code the window comes back; after the right one the join completes, and the laptop's code window closes by itself. |
| D4 | Known machine: Allow or Deny | dev-desktop Claude opens `dialog-test-2` to other machines; the laptop's Claude joins `dialog-test-2@dev-desktop`. Press Deny in dev-desktop's window. | The window has only Allow and Deny; the laptop's Claude is told it was declined; the seat stays free. |
| D5 | Removal reaches the other side (M7 again) | `pigeon peer remove office-laptop` on dev-desktop. | Within seconds the laptop's `pigeon peer list` shows no machines and its `pigeon topics` no longer lists `dialog-test@dev-desktop`. |
| D6 | Antigravity with another machine | A new Antigravity conversation on dev-desktop: *"Open the Pigeon topic dialog-test-agy to other machines."* Then the laptop's Claude joins `dialog-test-agy@192.168.2.20` (a first contact again after D5). | Antigravity is no longer refused; the code window on dev-desktop comes from Antigravity's session; the join completes and a message goes each way. |
| D7 | An agent forgets a machine (alpha.8) | After D6, or a new join of `dialog-test` from the laptop: laptop Claude: *"Forget dev-desktop."* | The agent calls `pigeon_leave` with `machine` and no prompt appears; both machines then show "Known machines: 0", and the shared topics end on both sides. |

Record any prompt that falls back to the terminal, and any window that does not appear in front. D7 needs 0.6.0-alpha.8 on both machines (the same install steps with `--version 0.6.0-alpha.8`).

### Results (2026-11-07, in progress)

| Test | Result | Evidence |
|---|---|---|
| D1 | Pass | A new Claude Desktop conversation on dev-desktop (`4391a3d4-…`) opened `dialog-test`; `pigeon topics` showed it open to other machines. Its agent already said the question would appear in a small Pigeon window. |
| D2 | Pass | The laptop's Claude asked to join `dialog-test@192.168.2.20`; "All dialogs opened": the contact window, then the code window. The agent said only that the request waited on its person in a Pigeon window, never mentioned a terminal, and did not see the code. |
| D3 | Pass (join) | The code entered in dev-desktop's window completed the join: dev-desktop knew office-laptop, `dialog-test` had `claude-code @office-laptop`, no request was left, and both agents were told. Whether a wrong code was tried, and whether the laptop's code window closed by itself, is not recorded yet. |
| D4 | Pass | `dialog-test-2@dev-desktop` from the known laptop: only an Allow window, no code; Allow joined it. `dialog-test-3`: Deny in the window; the laptop's agent was told it was declined and did not retry; the seat stayed free and the topic stayed open. |
| D5 | Pass (M7 fixed) | `pigeon peer remove office-laptop` on dev-desktop; the laptop then showed "Known machines: 0" and "No topics." dev-desktop kept its three topics with free seats, open to other machines again. |
| D6 | Pass | The Pigeon plugin 0.6.0-alpha.8 installed globally (copied to `%USERPROFILE%\.gemini\config\plugins\pigeon`, the first try of the production plugin): Antigravity listed it with its skill, MCP server, hooks and the **Pigeon wake** automation, running. A new Antigravity conversation (`15012ddd-…`) opened `dialog-test-agy` with `remote` (refused until alpha.6). The laptop's Claude joined `dialog-test-agy@192.168.2.20` as a first contact: "All three dialogs were shown with the code" (contact and code on the laptop, the code entry on dev-desktop from Antigravity's session). `pigeon status` showed `notification: Antigravity sidecar`. Round 1 from the laptop woke Antigravity, which replied "D6 round 2 of 2" and stopped; the laptop's Claude was woken for it. |
| D7 | Pass, with a wording fix | The laptop's Claude was told *"Forget dev-desktop."*: it called `pigeon_leave` with `machine`, no prompt appeared, and both machines showed "Known machines: 0"; `dialog-test-agy` on dev-desktop kept Antigravity with its seat free and was open again. The agent said "every topic with it has ended on both machines", from the result text, although dev-desktop's topics remained; 0.6.0-beta.1 says each topic stays on the machine that opened it. |

## 0.6.0-alpha.4 two-machine tests with agents (M1–M7)

Prepared on 2026-11-07; not run yet. They check alpha.4's joins between machines with real agents: Claude Desktop on both `dev-desktop` (192.168.2.20) and `office-laptop` (192.168.2.18; the user switched the laptop from Codex to Claude on 2026-11-07), on the same Wi-Fi network. They use the installed `pigeon` on both machines (the user chose real installs), new test conversations and topics named `remote-test*`.

### Setup

1. **dev-desktop:** Pigeon 0.6.0-alpha.4 installed from this branch, the service started and the peer listener open on 192.168.2.20:49374 (done on 2026-11-07).
2. **office-laptop (you):** close Claude, then in the repository:
   `git fetch`, `git switch alpha3-remote-connections`, `git pull`,
   `dotnet pack src/Pigeon.Cli/Pigeon.Cli.csproj --output artifacts`,
   `dotnet tool update --global Pigeon.Cli --version 0.6.0-alpha.4 --source ./artifacts`,
   `pigeon stop`, `pigeon start`, `pigeon peer listen` (note the address it prints; allow port 49374 for private networks if Windows asks).
3. **Forget each other**, so the first join uses a code: `pigeon peer remove office-laptop` on dev-desktop and `pigeon peer remove dev-desktop` on office-laptop.
4. **Agents:** a new Claude Desktop conversation on each machine, so both run alpha.4's MCP server.

### Tests

| # | Test | Steps | Pass when |
|---|---|---|---|
| M1 | Open a topic | Claude: *"Open the Pigeon topic remote-test to other machines. You may reply to Pigeon messages on that topic without asking me; replies only."* | Claude joins with `remote`; `pigeon topics` on dev-desktop shows it listed. |
| M2 | First contact asks the requester | Laptop Claude: *"Join the Pigeon topic remote-test@192.168.2.20. You may reply to Pigeon messages on that topic without asking me; replies only."* | The laptop's Claude shows Pigeon's prompt "Contact 192.168.2.20, a machine you have not joined before?"; after yes it shows a 6-digit join code to you, not to the agent; the agent reports only that the request was submitted. |
| M3 | The code completes the join | On dev-desktop, Pigeon's prompt appears in Claude: a new machine, office-laptop, wants to join remote-test with Claude Code. Enter the code from M2. | The join completes; each side's `pigeon peer list` shows the other; the topic is no longer listed. |
| M4 | Messages and wakes both ways | Claude: *"Send on remote-test: 'M4 round 1 of 3. Reply with the next round; stop after round 3.'"* | Rounds 2 and 3 happen with no typing, each Claude woken by its hook waiter; each sees the other as `@<machine>`, never a session ID. |
| M5 | A known machine needs only Allow | Claude opens `remote-test-2` to other machines; the laptop's Claude joins `remote-test-2@dev-desktop`. | Claude's prompt asks only Allow or Deny, with no code; after Allow the join completes. |
| M6 | Deny refuses | Claude opens `remote-test-3`; the laptop's Claude asks to join it; answer Deny. | The laptop's Claude is told it was declined; the seat stays free and the topic stays listed. |
| M7 | Leaving and forgetting | Laptop Claude: *"Leave remote-test."* Then `pigeon peer remove office-laptop` on dev-desktop. | Claude's side shows the seat free; after the removal, office-laptop's remaining topic with dev-desktop ends on both sides. |

The terminal fallback (`pigeon requests`, `pigeon allow`, `pigeon deny`) is used only if a prompt does not appear; record it if so.

### Results (2026-11-07, in progress)

| Test | Result | Evidence |
|---|---|---|
| M1 | Pass, with a gap | A new Claude Desktop conversation on dev-desktop (`1ea23a45-…`) joined `remote-test` with `remote`. `pigeon peer list` shows "open to other machines: remote-test", but `pigeon topics`, `pigeon status` and the agents' `pigeon_topics` did not say a topic is open to other machines (fixed in alpha.6). |
| M2 | Fail, fixed | The laptop's Claude Desktop asked to join `remote-test@192.168.2.20` twice; both times Pigeon reported that contacting 192.168.2.20 was not allowed, and no prompt appeared on either machine. **Cause:** Claude sessions started by the desktop app advertise MCP elicitation but cancel every prompt unseen, and Pigeon counts a cancel as Deny; the elicitation probe had covered only Claude Code in a terminal. **Fix:** Pigeon's MCP server recognizes these sessions (`CLAUDE_CODE_ENTRYPOINT=claude-desktop`) and never sends them prompts; their requests wait for `pigeon requests` / `pigeon allow` in a terminal, and the agent is told so. Re-run after the fix (0.6.0-alpha.5): the laptop's Claude answered that this app cannot show Pigeon's prompts and pointed to `pigeon requests`; a retry returned the same request (`68659f4438c6`) instead of a duplicate. On the laptop `pigeon allow 68659f4438c6` confirmed the contact and `pigeon requests --show` showed join code 860261. **Pass (terminal).** |
| M3 | Pass (terminal) | The request arrived on dev-desktop as `e5e55d8c625c` ("office-laptop (192.168.2.18:49374) wants to join remote-test with claude-code"); the user entered the code with `pigeon allow e5e55d8c625c 860261`: "Allowed. The other session is in the topic now." Both machines listed each other; the remote member showed as `claude-code @office-laptop`. `peer list` showed "last contact: never" right after the join (fixed in alpha.6). Before M3, a new conversation on dev-desktop took over `remote-test` from the old one at the user's choice, which a local caller can do through the CLI. |
| M4 | Pass | Three rounds with no typing, each Claude woken by its hook waiter; round 3 ended the exchange; nothing left to send. Both agents described each sent message as not yet delivered, from the send result's "Queued for the other machine" (reworded in alpha.6). |
| M5 | Pass | `remote-test-2@dev-desktop` from the known laptop arrived as `2fd4a2cf5f80` with no code; `pigeon allow 2fd4a2cf5f80` joined it. |
| M6 | Pass | `pigeon deny d87ac732abe8`: the laptop's Claude was told it was declined and did not retry; `remote-test-3` kept its free seat and stayed open to other machines. |
| M7 | Fail, fixed | Leaving `remote-test` on the laptop freed the seat on dev-desktop, which listed the topic again; the laptop's agent, given only `{"deleted":true}`, wrongly said the topic ended on both sides (alpha.6 explains it). `pigeon peer remove office-laptop` ended `remote-test-2` on dev-desktop, but **the laptop was never told**: it still listed dev-desktop and the topic, and "M7 after removal" sat in its outbox ("waiting to send: 1"), refused at the handshake and retried for a day. **Fix (alpha.6):** a removal sends a "forget" notice to the removed machine, which ends the topics and forgets this machine too. Re-run pending. |

## 0.6.0-alpha.2 Copilot tests (C1–C6)

Prepared and started on 2026-11-04 (results below). They check the Copilot plugin, including whether an extension shipped in an installed plugin loads (the probe used `.github/extensions`), in a new test repository, against the isolated service on port 49473 and a disposable database.

### Results (2026-11-04)

Run with VS Code 1.145 (Copilot harness; Copilot runtime started fresh), the frozen build and the `pigeon-dev` plugin installed with `copilot plugin install` (the CLI warned that direct installs from a local path are deprecated in favour of `plugin@marketplace`), in `C:\Projects\PigeonCopilotTest`, chat `f81ea48a-…`.

| Test | Result | Evidence |
|---|---|---|
| C1 | Pass | `pigeon_join` with only the topic, no permission prompt; `status` showed `copilot f81ea48a-… \| … \| notification: Copilot extension`. **The extension shipped in the installed plugin loads**, as the probe's `.github/extensions` one did. |
| C2 | Pass | Sent 12:03:13; the wake prompt appeared in the chat as a user message, the agent received, replied and completed with no prompt, and "C2 pong" arrived 12:03:18. |
| C3 | Pass | During a five-ping task (with a terminal approval), "C3 during a turn" waited with 0 attempts from 12:04:44 to 12:05:56; the wake followed the turn and "C3 pong" arrived 12:06:02. |
| C4 | Pass | A second chat (`04c796e1-…`) joined `pigeon-host-test-pair`; chat 1 joined and sent round 1. Each message woke the other chat with no typing and no prompt; `status` listed both chats with `notification: Copilot extension`, and every wake completed. The agents miscounted (chat 1 answered round 2 with "round 2 of 3" again), so the exchange took four messages; Pigeon delivered each once. |
| Full topic | Pass | A third chat's join on the full `pigeon-host-test-pair` was refused, naming both members. |
| C6 | Pass | In the GitHub Copilot app, a session (`31d0cf3e-…`) on the same repository joined `pigeon-host-test-app` with no permission prompt, and `status` showed `notification: Copilot extension` for it: the app loads the installed plugin's MCP server and extension too. A peer message sent 12:15:36 woke it within 4 s and "C6 pong" arrived 12:15:44; the app showed the wake prompt and the calls to `pigeon_receive`, `pigeon_send` and `pigeon_wake_complete` with no prompt. The app ran the session in its own worktree (`~\.copilot\copilot-worktrees\…`); identity comes from the session, so it made no difference. |
| C5 | Pass | `restart` at about 12:03:31, then "C5 after restart" at 12:03:33: the extension's repeated state restored the turn state, the wake was sent within 7 s with no prompt and "C5 pong" arrived 12:03:42. |

### What is prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Release build of the working tree on `alpha2-host-adapters`, version 0.6.0-alpha.2. |
| Dev plugin `pigeon-dev` | `experiments/copilot-dev/pigeon-dev` | Generated from `integrations/copilot-plugin` by `make_dev.py`: plugin, MCP server and extension named `pigeon-dev`, the frozen build, port 49473 and the test database. Otherwise identical. |
| Helper | `experiments/host-test/hosttest.py` | `start`, `status`, `send`, `inbox`, `restart`, `stop`. |

### Setup (needs your approval)

1. **Start the isolated service:** `python -I experiments/host-test/hosttest.py start` (background, until stopped). It joins the scripted peer `host-test-peer` to `pigeon-host-test`.
2. **Install the dev plugin for your user:** `copilot plugin install C:\Projects\Pigeon\experiments\copilot-dev\pigeon-dev`. Installed plugins load in every Copilot session (app, CLI and VS Code); this one talks only to the test service, and is removed in cleanup.
3. **Test repository:** create the empty folder `C:\Projects\PigeonCopilotTest` and `git init` it.
4. **You:** close VS Code, check that `copilot-runtime.exe` has exited (it can outlive VS Code; end it in Task Manager if needed), then open `C:\Projects\PigeonCopilotTest` in VS Code and start a new chat with **Copilot** as the Session Target.

### Tests

| # | Test | Steps | Pass when |
|---|---|---|---|
| C1 | Connect, no prompt | Chat 1: *"Use the pigeon-dev tools to join topic pigeon-host-test. You may reply to Pigeon messages on that topic without asking me; replies only."* | The join passes only the topic and no permission prompt appears; `status` shows `copilot <session> \| … \| notification: Copilot extension`. |
| C2 | Wake an idle chat | With the turn ended: `send "C2 ping: reply C2 pong on this topic."` | The wake prompt appears in the chat by itself, the agent receives, replies and completes with no prompt; `inbox` shows "C2 pong". |
| C3 | No interruption | Chat 1: *"Run `ping -n 8 127.0.0.1` five times, one after another, then tell me the total replies."* While it runs: `send "C3 during a turn: reply C3 pong."` | The message waits with 0 attempts during the turn; the task finishes; the wake follows. |
| C4 | Two chats | Chat 2 joins `pigeon-host-test-pair` with permission to reply; chat 1 joins and sends *"C4 round 1 of 3. Reply with the next round; stop after round 3."* | Rounds 2 and 3 with no typing; `status` lists both chats with `notification: Copilot extension`. |
| C5 | Service restart | `restart`, then `send "C5 after restart: reply C5 pong."` | The wake follows without a prompt (the extension repeats its state). |
| C6 | Copilot app | Open `C:\Projects\PigeonCopilotTest` in the GitHub Copilot app, start a session, join `pigeon-host-test-app`, and `send` to it from the peer after joining the peer to that topic. | The app session joins without a prompt and is woken like C2. |

### Cleanup

Done on 2026-11-04 after the user closed VS Code and the Copilot app (the plugin and the app's worktree were in use until then): the service was stopped and its data removed, `pigeon-dev` uninstalled, and the test repository, the Copilot app's worktree for the C6 session and the probe workspace `C:\Projects\PigeonCopilotProbe` deleted. The probe's logs in `experiments/copilot-probe/logs` are kept (ignored by Git). The test chats were left to the user.

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. `copilot plugin uninstall pigeon-dev`.
3. Delete `C:\Projects\PigeonCopilotTest`, the test chats, and the Copilot app's worktree for the C6 session (under `~\.copilot\copilot-worktrees`).

## 0.6.0-alpha.2 OpenCode tests (O1–O6)

Prepared and started on 2026-11-04 (results below). They check the OpenCode 2 plugin with a frozen build in a new test project, against the isolated service on port 49473 and a disposable database. The dev plugin is installed only in the test project (`.opencode/plugins`), so your other projects and your OpenCode configuration are not changed. Your installed `pigeon` tool and its service on port 49372 are not used.

### Results (2026-11-04)

Run with OpenCode 2.2.5 (CLI in Windows Terminal; Desktop also installed), the frozen build and the generated `pigeon-dev` plugin in `C:\Projects\PigeonOpenCodeTest`, in one session (`ses_e5afb0fe…`).

| Test | Result | Evidence |
|---|---|---|
| O1 | Pass | `pigeon-dev.pigeon_join [topic=pigeon-host-test]` with no session ID; `status` showed `opencode ses_e5afb0fe… \| … \| notification: OpenCode plugin`. OpenCode's log showed the plugin loaded and `mcp connected server=pigeon-dev tools=6`. |
| O2 | Pass | Sent 00:26:11; the wake was sent within 6 s and appeared as a user prompt; the agent called `pigeon_receive`, `pigeon_send` and `pigeon_wake_complete` with that generation, and "O2 pong" arrived 00:26:25. No approval prompts. |
| O3 | Fail, fixed, pass on re-run | First run (00:27): the wake was claimed 2 s after the send while the five-ping task ran. OpenCode showed it as "1 queued" and ran it after the task's answer, so the task was not derailed, but the plugin had never reported the session busy. **Cause:** OpenCode 2.2.5 does not publish `session.status` to plugins; a temporary trace of event types and field names (no content) showed turns as `session.execution.started` and `session.execution.succeeded` (the binary also has `failed` and `interrupted`). **Fix:** the plugin maps those events to busy and idle (`turnState`, with tests) and still handles `session.status`. Re-run (09:32:49): the message waited with 0 attempts for about 50 s while the pings ran; the wake followed the turn and "O3 pong" arrived 09:33:42. |
| O4 | Pass | A second session (`ses_e590358d…`) joined `pigeon-host-test-pair`; session 1 joined and sent round 1. Round 1 woke session 2, round 2 woke session 1, round 3 woke session 2, which stopped as asked, all with no typing. `status` listed both sessions as connected with `notification: OpenCode plugin`, every wake completed and nothing was left waiting. |
| Full topic | Pass | Extra, run by the user: a third session's `pigeon_join` on the full `pigeon-host-test-pair` was refused, naming both members and how to free a seat; the topic kept its two members. |
| O6 | Pass on re-run | First run (09:40): one OpenCode Desktop window was still open (noticed by the user afterwards), so it did not test a closed OpenCode. Re-run after the user quit OpenCode Desktop completely: no OpenCode Desktop process remained, but OpenCode's shared background service (`opencode-cli.exe serve --service`, started by Desktop at 00:08) kept running with the test MCP server under it. "O6 again, OpenCode closed", sent 09:51:03, woke session 1 through that service with no client open, and "O6 pong" arrived 09:51:11. So OpenCode sessions stay reachable while OpenCode's background service runs, even with no window or terminal open. |
| O5 | Pass | `restart` at 09:33:50, then "O5 after restart" at 09:33:52: with no prompt, the plugin's repeated idle report restored the turn state, the wake was sent within 5 s and "O5 pong" arrived 09:34:04. |

### What is prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Release build of the working tree on `alpha2-host-adapters` (after `d4dd49c`, with the Antigravity fixes and the OpenCode adapter), version 0.6.0-alpha.2. |
| Dev plugin `pigeon-dev` | `experiments/opencode-dev/pigeon-dev` | Generated from `integrations/opencode-plugin/pigeon` by `make_dev.py`: plugin and MCP server named `pigeon-dev`, the frozen build, port 49473 and the test database. Otherwise identical. |
| Helper | `experiments/host-test/hosttest.py` | As for the Antigravity tests: `start`, `status`, `send`, `inbox`, `restart`, `stop`. |

### Setup (needs your approval)

1. **Start the isolated service:** `python -I experiments/host-test/hosttest.py start` (background, until stopped). It joins the scripted peer `host-test-peer` to `pigeon-host-test`.
2. **Test project:** create the empty folder `C:\Projects\PigeonOpenCodeTest` and copy `experiments\opencode-dev\pigeon-dev` into it as `.opencode\plugins\pigeon-dev`.
3. **You:** start OpenCode in that folder (`opencode` in a terminal there, or open the folder in OpenCode Desktop) and start a new session. If your own OpenCode configuration already has a Pigeon MCP server, tell the agent to use the **pigeon-dev** tools.

### Tests

| # | Test | Steps | Pass when |
|---|---|---|---|
| O1 | Connect without a session ID | Session 1: *"Join topic pigeon-host-test with the pigeon-dev tools. You may reply to Pigeon messages on that topic without asking me; replies only."* | The join passes only the topic; `status` shows `opencode ses_… \| topics: pigeon-host-test \| … \| notification: OpenCode plugin`. |
| O2 | Wake an idle session | With the turn ended: `send "O2 ping: reply O2 pong on this topic."` | Within about 5 s a "Pigeon wake generation …" prompt appears; the agent receives, replies and completes; `inbox` shows "O2 pong". |
| O3 | No interruption during a turn | Session 1: a task of several steps, for example *"Run `ping -n 8 127.0.0.1` five times, one after another, then tell me the total replies."* While it runs: `send "O3 during a turn: reply O3 pong."` | The message waits with 0 attempts while the session is busy; the task finishes normally; the wake follows about 2 s after the turn ends. |
| O4 | Two sessions | Session 2 in the same project joins `pigeon-host-test-pair` with permission to reply; session 1 joins it too and sends *"O4 round 1 of 3. Reply with the next round; stop after round 3."* | Rounds 2 and 3 happen without typing; `status` lists both sessions as connected. |
| O5 | Service restart | `restart`, then `send "O5 after restart: reply O5 pong."` | Unlike Antigravity, no prompt is needed: within a few seconds the plugin repeats the session's idle status, the wake arrives and "O5 pong" follows. |
| O6 | OpenCode clients closed | Close every OpenCode window and terminal, then `send "O6 while closed: reply O6 pong."` and `status`. Then reopen the session. | Record what happens: OpenCode's shared background server may keep running and wake the session with no client attached (the reply then appears when you reopen it), or the plugin stops and the message waits. |

### Cleanup

Done on 2026-11-04: the service was stopped and its data removed, the test MCP server under OpenCode's service was stopped, and the test project's contents were deleted. The empty folder `C:\Projects\PigeonOpenCodeTest` stays until OpenCode's background service, which holds it open, restarts; the dev plugin also stays loaded there until then. The test sessions were left to the user.

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. Delete `C:\Projects\PigeonOpenCodeTest` and its OpenCode sessions.

## 0.6.0-alpha.2 Antigravity tests (A1–A7)

Prepared and started on 2026-11-03 (results below). They check the Antigravity App adapter with a frozen build in a new test workspace, against the isolated service on port 49473 and a disposable database. Only the wake sidecar is installed outside the repository, because Antigravity loads sidecars only from its global folder; it talks only to the isolated service. Your installed `pigeon` tool, its service on port 49372 and your other workspaces are not used.

### Results (2026-11-03)

Run with the frozen build of `d4dd49c` (service, MCP server, hooks) and, from A6 on, the sidecar from `artifacts/host-test/bin-sidecar` (the same build plus the heartbeat fix), in one new conversation (`51137800-…`) in `C:\Projects\PigeonAntigravityTest`.

| Test | Result | Evidence |
|---|---|---|
| A1 | Pass, with a bug | `pigeon_join` with only the topic joined conversation `51137800-…` to `pigeon-host-test`. **Bug:** `status` said the sidecar was not running although it was polling and sending wakes: its heartbeat timer started at `long.MinValue`, so `TickCount64 - lastHeartbeat` overflowed and the heartbeat was never due. Fixed, with a unit test. |
| A2 | Pass | Sent 22:23:52; the sidecar's wake was accepted by 22:23:57. The conversation then waited for approval of `pigeon_receive`, `pigeon_send` and `pigeon_wake_complete`, one prompt per tool (allowing `pigeon_join` had not allowed them). After approval "A2 pong" arrived at 22:25:42 and the wake completed. |
| A3 | Pass | Antigravity moves any command over 10 s into the background and ends the turn, so one long command never keeps a turn busy. The test message, sent at 22:38:05 during a 17 s turn that started `ping -n 90`, waited with 0 attempts until about 22:38:17; the wake then came as its own turn after that turn ended ("Message from System"), the agent replied "A3 pong" (22:38:27), noted the ping was still running in the background, and later reported its result. The user's task was not derailed. |
| A4 | Pass, with a bug | A second conversation (`db818028-…`) in the same workspace, served by the same Pigeon MCP process, joined `pigeon-host-test-pair` under its own ID. Round 1 (conversation 1) woke conversation 2, round 2 woke conversation 1, round 3 woke conversation 2, which stopped as asked; each wake completed with no typing and nothing was left waiting. **Bug:** `status` listed only one connected session, the conversation that made the last call: the MCP process reports one session per bridge, so with per-call identity it undercounts Antigravity conversations, and a send's "recipient is connected" can be wrong for them. Wakes do not depend on it. |
| A5 | Pass | `restart` at 22:46:55 dropped the in-memory turn state. "A5 after restart", sent 22:46:56, waited with 0 attempts (also through activity in a third conversation) until the user's next prompt in conversation 1 ("Say hello."). After that turn the wake came as its own turn, and "A5 pong" arrived at 22:49:27; the wake completed. |
| A6 | Pass | With the sidecar off, its process exited and "A6 while off", sent 22:27:59, waited with 0 attempts. Turned on at about 22:30:15 with the fixed build: `status` showed `notification: Antigravity sidecar`, "A6 pong" arrived at 22:30:22 and the wake completed. |
| A7 | Fail, fixed, pass on re-run | After the user quit Antigravity, its language server and the conversations' Pigeon MCP server had exited, but the test sidecar (PID 43940) was still running with its parent gone; it would have kept polling and reporting itself, so `status` would claim a sidecar. Its loop had no exit except Ctrl+C, so this was not a delay. It was stopped with `taskkill`. **Fix:** the sidecar now watches the process that started it (the same parent watch as the Claude hook waiter) and stops when it exits; a new integration test kills only the outer of two Git Bash shells and checks that the sidecar exits. **Re-run: pass.** With the fixed build in `bin-sidecar`, the sidecar ran as PID 16948 under `language_server.exe` (48740) at 23:07:48; after the user quit Antigravity, both were gone by 23:09:20 and no test sidecar was left. |
| A8 | Pass | Extra test, run by the user: a third conversation in the workspace tried to join the full `pigeon-host-test-pair`. It was refused; the error named both members (`db818028-…` and `51137800-…`) and how to free a seat, and the topic kept its two members. |
| Re-check | Pass | After the fixes, with both builds rebuilt (2026-11-03 23:14 for the service, 23:41 for the sidecar): two new conversations A (`ef8e92ad-…`) and B (`e5a62906-…`) in the test workspace joined `pigeon-recheck` through one MCP process. `status` listed both as connected, and A's `pigeon_send` returned `"delivery":"sent"`. B's wake waited for approval of `pigeon_receive` (new conversations asked again, so the earlier approvals did not cover them); after approval B replied, A woke and acknowledged, and both wakes completed with nothing waiting. |

### What is prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Release build of `d4dd49c`, version 0.6.0-alpha.2. Rebuilding the solution does not touch it. |
| Dev plugin `pigeon-dev` 0.6.0-alpha.2.dev.1 | `experiments/antigravity-dev/plugin/pigeon-dev` | Workspace plugin: MCP server `pigeon-dev` (frozen build, `PIGEON_RUNTIME=antigravity`, port 49473), the three hooks and the connect skill. Same as production except the port, the binary and names. |
| Hook wrapper | `experiments/antigravity-dev/hook.cmd` | Sets the test port and database and runs `pigeon hook <event> antigravity`. Antigravity runs hooks through `cmd /c`, which has no per-hook environment. |
| Dev sidecar `pigeon-dev-wake` | `experiments/antigravity-dev/sidecar/pigeon-dev-wake` | Runs `pigeon sidecar antigravity` from the frozen build with the test port in its `env`. Shows as **Pigeon dev wake** in Automations. |
| Fixed sidecar build | `artifacts/host-test/bin-sidecar/pigeon.exe` (ignored by Git) | Added during the run: the frozen build plus the heartbeat fix. The service and the MCP server lock `bin`, so the dev sidecar was pointed here. |
| Helper | `experiments/host-test/hosttest.py` | As for E1–E6, plus `sidecars` (test sidecar processes and their parents) and `restart` (restart the isolated service, keeping its data). |

### Setup (needs your approval)

1. **Start the isolated service:** `python -I experiments/host-test/hosttest.py start`. It runs in the background until stopped, and joins the scripted peer `host-test-peer` to `pigeon-host-test`.
2. **Test workspace:** create the empty folder `C:\Projects\PigeonAntigravityTest` and copy `experiments\antigravity-dev\plugin\pigeon-dev` into it as `.agents\plugins\pigeon-dev`.
3. **Sidecar (global folder):** copy `experiments\antigravity-dev\sidecar\pigeon-dev-wake` to `%USERPROFILE%\.gemini\config\sidecars\pigeon-dev-wake`.
4. **You:** restart Antigravity, open `C:\Projects\PigeonAntigravityTest`, and enable **Pigeon dev wake** in the Automations dashboard.
5. **You:** open a new conversation there. When it first calls a `pigeon-dev` tool, allow the tools for this project, so wakes do not wait for approval.

### Tests

Run the helper from the repository root as `python -I experiments/host-test/hosttest.py <command>`.

| # | Test | Steps | Pass when |
|---|---|---|---|
| A1 | Connect without a session ID (2 min) | Conversation 1: *"Use the pigeon-dev connect skill to join topic pigeon-host-test. You may reply to Pigeon messages on that topic without asking me; replies only."* | `pigeon_join` is called with only the topic; `status` shows `antigravity <conversation id> \| topics: pigeon-host-test \| … \| notification: Antigravity sidecar`. |
| A2 | Wake an idle conversation (3 min) | With the turn ended: `send "A2 ping: reply A2 pong on this topic."` | Within about 5 s the conversation gets the fixed "Pigeon wake generation …" message, calls `pigeon_receive`, replies and calls `pigeon_wake_complete`; `inbox` shows "A2 pong"; `status` shows nothing waiting and 1 attempt. It does not investigate the wake. |
| A3 | No wake during a turn (5 min) | Conversation 1: *"Run `ping -n 40 127.0.0.1` and then tell me how many replies came back."* While it runs: `send "A3 during a turn: reply A3 pong."` and `status`. | While the turn runs, `status` shows the message waiting with 0 attempts. The ping task finishes and is answered normally; about 2 s after the turn ends the wake arrives and "A3 pong" follows. |
| A4 | Two conversations, one server (10 min) | Open conversation 2 in the same workspace: *"Join pigeon-host-test-pair with the pigeon-dev tools. You may reply to Pigeon messages on that topic without asking me; replies only."* Conversation 1: *"Join pigeon-host-test-pair too, then send on it: 'A4 round 1 of 3. Reply with the next round; stop after round 3.' Reply to replies the same way until round 3."* | Rounds 2 and 3 happen with no further typing; `status` shows two `antigravity` sessions with their own IDs, each on its own topics; nothing waiting afterwards. |
| A5 | Service restart (5 min) | `restart`, then `send "A5 after restart: reply A5 pong."` and `status`. Then type *"Say hello."* in conversation 1. | Before your prompt the message waits with 0 attempts (turn state is in memory and was lost). After your prompt's turn ends, the session is restored, the wake arrives and "A5 pong" follows. |
| A6 | Sidecar off and on (5 min) | Disable **Pigeon dev wake** in Automations; `sidecars`, `status`; `send "A6 while off: reply A6 pong."`. Then enable it again. | While off: no test sidecar process, `status` shows `notification: none (the Pigeon wake sidecar is not running…)` and the message waits with 0 attempts. After enabling: the wake arrives and "A6 pong" follows. |
| A7 | Antigravity exits (2 min) | Quit Antigravity completely, then `sidecars`. | No test sidecar process is left. If one is, record it (the sidecar would then need to watch its parent) and stop it with `taskkill /PID <id>`. |

**Notes from the run.** A conversation started outside the test workspace does not load the workspace plugin; one such conversation then explored the plugin files and asked to call Antigravity's internal language-server API with its CSRF token, which the user refused. With the per-conversation approval prompts, the first wake in each new conversation waits for the user.

**Not covered:** installing the production plugin from Antigravity's global plugin folder (`agy plugin install`, the plugin-bundled `pigeon/wake` sidecar) needs the installed tool updated to alpha.2; it is left for the release check. A wake lost to a deleted conversation (retried every 2 minutes, then given up after 3 attempts) is covered by the integration tests only.

**Finding while preparing.** With no service running, each hook takes about 2.3 s, because a refused connection to 127.0.0.1 takes about 2 s on Windows. `PreInvocation` runs before every model call, so a stopped service would slow every Antigravity model call. The Claude hooks have the same cost per hook, but they run only at session start and turn end.

### Cleanup

Done on 2026-11-04: the service was stopped and its data removed, and the dev sidecar folder and the test workspace were deleted. The sidecar's enabled setting in Antigravity's configuration and the test conversations were left to the user.

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. Disable **Pigeon dev wake** in Automations and delete `%USERPROFILE%\.gemini\config\sidecars\pigeon-dev-wake`.
3. Delete `C:\Projects\PigeonAntigravityTest` and archive or delete its conversations.

## 0.5.0-beta.2 end-to-end tests (E1–E6)

Run on 2026-11-03; results below. They repeat the earlier host tests with the two-member topics and the new agent tools (`pigeon_join`, `pigeon_send`, `pigeon_receive`, `pigeon_wake_complete`), and with the hook wrapper now started through `sh`.

### Results (2026-11-03)

Run with the frozen Release build of `f77aab0` (0.5.0-beta.2), the dev plugins 0.5.0-beta.2.dev.1, Claude Desktop (Claude Code 2.1.309) and the Codex app, in one new Claude test conversation (`94260bcf-…`) and one new Codex chat (`01a1a254-…`).

| Test | Result | Evidence |
|---|---|---|
| E1 | Pass | Sent 11:44:52; the conversation woke by itself, received and completed the wake, and "E1 pong" reached the peer 6 s later. |
| E2 | Pass | Round 1 (Claude) 11:54:52 woke Codex through its queue; round 2 (Codex) 11:55:04 woke Claude through the hook waiter; round 3 (Claude) 11:55:08 woke Codex, which acknowledged and stopped. Each message was delivered once and acknowledged on completion; no typing after the first prompt. |
| E3 | Pass | Waiter armed 11:45:16; renewal after 3 idle minutes and a replacement waiter at 11:48:14; "E3 after renewal" sent 11:48:19 and "E3 pong" arrived 11:48:24. Repeated at 12:27 in a new conversation (`9bc29461-…`) to check the renewal turn: its transcript shows one line ("The Pigeon waiter is renewing after 3 idle minutes…") with no tool calls, polling or reply; the next message then woke the replacement waiter, which called `pigeon_receive`, `pigeon_send` and `pigeon_wake_complete`. |
| E4 | Pass | Killed only the outer hook `bash.exe` at 11:48:51; status showed `orphaned` at 11:48:54 and no waiter remained. "E4 after kill" waited with 0 wake attempts; after the user's next prompt the waiter re-armed and "E4 pong" arrived at 11:52:06. |
| E5 | Pass | One message on each of Claude's two topics (11:56:06); both were received and acknowledged under one wake generation within 4 s. The second message was sent through the CLI under the Codex test session's ID. |
| E6 | Pass | Codex's `pigeon_join` on the full `pigeon-host-test` was refused; the error named `host-test-peer` and the Claude session, and the topic kept its two members. |

**H4, H5 and H6** (the 12-hour renewal, Codex recovery and Claude reopen) were run by the user and reported as passed on 2026-11-03. Their logs are not in this repository.

**Finding.** With the hook started as `sh "…/hook-wait.sh"`, the waiter's launcher chain now reaches Claude Code: `pigeon.exe <- sh.exe <- sh.exe <- sh.exe <- bash.exe <- bash.exe <- claude.exe`. With the earlier direct command, the launcher above the hook shells exited at once and the chain ended at the top shell, so the waiter did not watch `claude.exe`. The waiter now also stops if the conversation process exits while its hook shells survive (not tested directly).

### What is prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Release build of `f77aab0`, version 0.5.0-beta.2. Rebuilding the solution does not touch it. |
| Claude dev plugin `pigeon-dev` 0.5.0-beta.2.dev.1 | `experiments/claude-hook-waiter-dev` | Hooks generated from production beta.2 (`sh "…/hook-wait.sh"`), with port 49473, the frozen build and a 3-minute renewal (hook timeout 5 minutes). Skill text is production's plus a dev header. |
| Codex dev plugin `pigeon-dev` 0.5.0-beta.2.dev.1 | marketplace `pigeon-codex-dev` at `experiments/codex-plugin-dev` | MCP server `pigeon-dev` (queue mode) on port 49473 with the frozen build, and the MCP restore hooks. |
| Helper | `experiments/host-test/hosttest.py` | `start` joins a scripted peer (`host-test-peer`) to topic `pigeon-host-test`; `send <text>` sends from it; `inbox` receives its replies; `status`, `waiters`, `kill-outer-shell`, `stop`. |

### Setup (needs your approval)

1. **Start the isolated service:** `python -I experiments/host-test/hosttest.py start`. It runs in the background until stopped.
2. **Claude test folder:** create the empty folder `C:\Projects\PigeonHookWaiterTest`, and from it:
   - `claude plugin marketplace add "C:\Projects\Pigeon\experiments\claude-hook-waiter-dev" --scope local`
   - `claude plugin install pigeon-dev@pigeon-hook-waiter-dev --scope local`
   - `claude plugin disable pigeon@pigeon --scope local`, so the production plugin (installed for your user) does not also load in the test conversation.
3. **You:** open a new Claude Desktop conversation in `C:\Projects\PigeonHookWaiterTest` and type `/pigeon-dev:connect pigeon-host-test`.
4. **You, for E2 and E6:** in Codex add the marketplace `C:\Projects\Pigeon\experiments\codex-plugin-dev`, install `pigeon-dev`, accept its hooks, and open a new chat. Tell it to use the **pigeon-dev** tools (your production `pigeon` plugin is also loaded there). Like any Codex plugin it loads in every chat; remove it after the tests.

### Tests

Run the helper from the repository root as `python -I experiments/host-test/hosttest.py <command>`.

| # | Test | Steps | Pass when |
|---|---|---|---|
| E1 | Claude wakes for a message (about 5 min) | After the connect turn: `status` shows the Claude session in `pigeon-host-test` with `notification: claude-hook`. Then `send "E1 ping: reply E1 pong on this topic."` | The conversation wakes by itself, calls `pigeon_receive` and `pigeon_wake_complete`, and replies with `pigeon_send`; `inbox` shows "E1 pong"; `status` shows nothing waiting. |
| E2 | Claude ↔ Codex, three rounds (about 10 min) | Codex chat: *"Use the pigeon-dev tools to join topic pigeon-host-test-pair. You may reply to Pigeon messages on that topic without asking me; replies only."* Claude conversation: *"Join pigeon-host-test-pair too, then send on it: 'E2 round 1 of 3. Reply with the next round; stop after round 3.' Reply to replies the same way until round 3."* | Rounds 2 and 3 happen with no further typing; each side wakes on its own path (Codex queue, Claude hook); `status` shows nothing waiting afterwards. |
| E3 | Renewal (about 5 min) | Leave the Claude conversation idle for more than 3 minutes, then `send "E3 after renewal: reply E3 pong."` | One renewal turn ends quietly (no polling, no reply); a replacement waiter appears in `waiters`; the message then wakes it and "E3 pong" arrives. |
| E4 | Outer hook shell killed (about 5 min) | `waiters` shows `pigeon.exe <- sh.exe <- … <- bash.exe`. `kill-outer-shell`, then `kill-outer-shell --yes`. Then `send "E4 after kill: reply E4 pong."` | `status` shows `none (last claude-hook orphaned …)` within seconds; the message waits with 0 wake attempts; after your next prompt in the conversation the waiter re-arms and "E4 pong" arrives. |
| E5 | Two topics, one wake (about 3 min) | With the Claude conversation idle: `send "E5 from the peer"` and, at the same time, ask Codex to send "E5 from Codex" on `pigeon-host-test-pair`. | Claude wakes once and receives both messages in one `pigeon_receive`, each naming its topic. |
| E6 | Full topic (1 min) | Ask the Codex chat to join `pigeon-host-test` with the pigeon-dev tools. | Refused: the error names the peer and the Claude session and says how to free a seat. |

### Cleanup

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. From `C:\Projects\PigeonHookWaiterTest`: `claude plugin uninstall pigeon-dev@pigeon-hook-waiter-dev --scope local` and `claude plugin marketplace remove pigeon-hook-waiter-dev`; then delete `%USERPROFILE%\.claude\plugins\cache\pigeon-hook-waiter-dev` and the test folder.
3. In Codex, uninstall `pigeon-dev`, remove the `pigeon-codex-dev` marketplace and archive the test chat.
4. Archive the Claude test conversation.

## Results of the 0.1.0-alpha.19 tests H1–H3 (2026-10-31)

Run with the frozen build of `2db7c84` (0.1.0-alpha.19), dev plugin 0.4.0-dev.2 and Claude Code 2.1.309 in Claude Desktop, in one new test conversation (`b1f82365-…`, later closed and deleted by the user). H1b used a second new conversation (`bb5cce88-…`) and a new Codex chat (`01a19407-…`) with the dev Codex plugin 0.2.0-dev.2.

| Test | Result | Evidence |
|---|---|---|
| H1a | Pass | Sent 17:39:36; the conversation woke by itself, confirmed, received, acknowledged and completed; "H1a pong" reached the peer. |
| H2 | Pass | Waiter armed 17:39:59; renewal at 17:43:02; replacement waiter armed 17:43:07. "H2 after renewal", sent 17:43:18, woke the replacement and "H2 pong" arrived by 17:43:42. Not yet confirmed: that the renewal turn ended without polling or replying (needs a look at the test conversation). |
| H3 | Pass | Killed only the top hook shell at 17:44:47; status showed `orphaned` at 17:44:48 and the waiter exited by 17:44:51. "H3 after kill" queued with 0 attempts. The user's next prompt re-armed the waiter and "H3 pong" arrived by 17:46:29. |
| H1b | Pass | Codex alone: a peer message at 18:24:57 woke the Codex chat through the plugin's queue adapter and its reply arrived at 18:25:14, with no user action (the user had told that chat it may reply to Pigeon messages; without that it acknowledged but declined to reply, as designed). Round trip: after one user prompt to start it, Claude sent round 1, Codex woke (18:31) and replied round 2, Claude woke through its hook waiter and replied round 3, Codex woke (18:32) and acknowledged. Every wake was confirmed, drained, acknowledged and completed; status afterwards showed both sessions `completed`, nothing pending, and Claude's waiter re-armed. |

**Codex plugin packaging.** Codex did not start the server from a root `mcp.json` with `"type": "stdio"` (0.2.0-dev.1: the skill installed, the server and hooks did not). It loads local servers the way its bundled plugins do: `.codex-plugin/plugin.json` with `"mcpServers": "./.mcp.json"` and a `.mcp.json` of `command`/`args`/`env` entries. With that layout (0.2.0-dev.2, after an uninstall and reinstall) the server, the skill and both `mcp_tool` restore hooks loaded. The plugin is enabled for all projects, so Codex starts its server for every loaded chat; those bridges stay unregistered unless a chat joins. The production Codex plugin still uses the root layout and needs the same change.

**Closing a conversation.** When the user closed the first test conversation, its `claude.exe`, bridge and waiter exited with it and nothing was left behind. Unarchiving a conversation did not start its process.

**Finding.** On the host, the process that launches each hook's Git Bash shell exits immediately, so a waiter's chain is `pigeon.exe <- bash <- bash <- bash` and ends at the top shell without reaching `claude.exe`. The waiter stops when its hook shells die (H3), but would not notice a conversation process that exits while those shells survive. The helper's `kill-outer-shell` now targets the top `hook-wait.sh` shell instead of requiring a `claude.exe` parent.

## Earlier runbook for H1–H3 (alpha.19)

### What was prepared

| Piece | Where | Notes |
|---|---|---|
| Frozen test build | `artifacts/host-test/bin/pigeon.exe` (ignored by Git) | Debug build of commit `2db7c84`. Rebuilding the solution does not touch it. |
| Isolated service | port 49473, database in `artifacts/host-test/data` | Started and stopped with the helper. Your normal service on 49372 is not used. |
| Claude dev plugin `pigeon-dev` 0.4.0-dev.2 | `experiments/claude-hook-waiter-dev` | Generated from the production 0.4.0 hooks, with the same direct `hook-wait.sh` invocation and the same wrapper script. Only the port, the binary and a 3-minute renewal deadline (hook timeout 5 minutes) differ. |
| Codex dev plugin `pigeon-dev` 0.2.0-dev.2 | marketplace `pigeon-codex-dev` at `experiments/codex-plugin-dev` (plugin in `plugin/`, `.codex-plugin` layout with a `.mcp.json` stdio server, as Codex's bundled local-server plugins use; 0.2.0-dev.1 used a root `mcp.json` and Codex did not start its server) | MCP server `pigeon-dev` on port 49473 with the queue adapter, plus the MCP restore hooks. The session-start command hook is left out because it cannot be pointed at another port portably. Only needed for H1b. |
| Helper | `experiments/host-test/hosttest.py` | Runs the service, a scripted peer (session `host-test-peer`, already joined to `pigeon-host-test`) and the H3 kill. Updated for the two-member topics of 0.5.0-beta.1; the results below were recorded with the earlier commands. |

### Actions that needed approval

1. **Start the isolated service.** It runs in the background until it is stopped:
   `python -I experiments/host-test/hosttest.py start`
2. **Install the Claude dev plugin at local scope** in an empty test folder, so no other conversation loads it:
   `claude plugin marketplace add "C:\Projects\Pigeon\experiments\claude-hook-waiter-dev" --scope local`
   `claude plugin install pigeon-dev@pigeon-hook-waiter-dev --scope local`
   Run both from `C:\Projects\PigeonHookWaiterTest`.
3. **You open a new Claude Desktop conversation** in `C:\Projects\PigeonHookWaiterTest` and type `/pigeon-dev:connect pigeon-host-test`. Earlier, Claude's auto-mode classifier refused to let me start a test conversation, so you start it.
4. **H1b only:** you add the marketplace root `C:\Projects\Pigeon\experiments\codex-plugin-dev` to Codex (`codex plugin marketplace add` or the ChatGPT desktop app), install `pigeon-dev` from it, accept its hook-trust review, and in a new Codex conversation ask it to connect to `pigeon-host-test`. This is also the first host check of a Codex plugin install: note whether the hook review appears and whether its `mcp_tool` restore hooks run (Codex's documentation shows only `command` hooks). The production Pigeon server was earlier configured as a standalone MCP server, not through the plugin.

### Tests H1–H3

Run the helper as `python -I experiments/host-test/hosttest.py <command>`. "Status" below means its `status` command.

#### H1a: Claude and the scripted peer (about 10 minutes)

1. After the connect turn ends, status shows `notification: claude-hook` for the Claude session.
2. `send "H1a ping"` (the peer is the test conversation's only partner in the topic).
3. **Pass:**
   - the conversation wakes by itself with the fixed Pigeon instruction;
   - it confirms, receives, acknowledges and completes the wake;
   - status shows 1 attempt and no queued message;
   - if asked in the message, it replies with `pigeon_send` on the same topic, and `inbox` shows the reply.

#### H1b: Claude and Codex (about 15 minutes; needs action 4)

1. Ask the Codex conversation to send a message to the Claude session.
2. **Pass:**
   - Claude wakes, processes the message and replies;
   - the reply wakes Codex through its queue adapter, and Codex confirms, receives, acknowledges and completes;
   - no user typing is needed after the first request.

#### H2: renewal and re-arm (about 10 minutes)

1. Leave the Claude conversation idle for more than 3 minutes.
2. **Pass:**
   - one renewal turn starts by itself ("needs renewal ... This is not a message"), and Claude ends it without polling or replying;
   - status shows `claude-hook` again;
   - `send "H2 after renewal"` then wakes the conversation through the replacement waiter;
   - the wake attempt count stays at 0 through the renewal, until that message.

#### H3: outer hook shell killed (about 10 minutes)

1. With a waiter armed, run `waiters` to see its chain: `pigeon.exe <- bash.exe <- bash.exe <- bash.exe` (on the host the launcher above the top shell has already exited).
2. Run `kill-outer-shell` to preview, then `kill-outer-shell --yes`. This kills only the top hook shell; the waiter must notice and stop on its own.
3. **Pass:**
   - within a few seconds, status shows `none (last claude-hook orphaned ...)`;
   - after `send "H3 after kill"`, the attempt count stays 0 (nothing was claimed into a dead pipe);
   - after your next prompt in the conversation, the Stop hook re-arms and the message is delivered.

### Cleanup (alpha.19)

1. `python -I experiments/host-test/hosttest.py stop --clean`.
2. From `C:\Projects\PigeonHookWaiterTest`:
   - `claude plugin uninstall pigeon-dev@pigeon-hook-waiter-dev --scope local`
   - `claude plugin marketplace remove pigeon-hook-waiter-dev`
3. Delete `%USERPROFILE%\.claude\plugins\cache\pigeon-hook-waiter-dev` and the test folder.
4. Archive the test conversations. If H1b ran, remove the Codex dev plugin.
