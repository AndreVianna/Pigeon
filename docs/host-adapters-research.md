# Coding-agent hosts for 0.6.0: market and fit

Research of 2026-11-03, for choosing which host adapters to build after Claude Code and Codex. It uses web sources only (official docs, changelogs, repositories and published usage figures). Nothing was installed or signed into. Points marked **unverified** need a host test before we rely on them. The six parts a host integration needs, and the earlier Antigravity and Cursor findings, are in [host integrations research](host-integrations-research.md).

Each host gets a verdict:

- **A**: a documented way to start a turn in the *open, idle* session the user is watching, so Pigeon can reach parity with Claude and Codex.
- **B**: messaging and restore, with delivery only when a session starts or a turn ends (a `Stop`-hook long-poll or a headless turn). This is how Cursor ships.
- **C**: not suitable now.

## Market

Survey bases differ, so figures from different sources are not comparable. Install counts are VS Code Marketplace numbers and star counts GitHub numbers, both read on 2026-11-03.

| # | Agent | Usage evidence |
|---|---|---|
| 1 | GitHub Copilot (VS Code, CLI) | 20M all-time users (Jul 2025); 4.7M paid (Jan 2026); 59% of Stack Overflow 2026 respondents |
| 2 | Claude Code | 66% of agent users (Stack Overflow 2026); 29.4M extension installs |
| 3 | Cursor | 18% workplace use (JetBrains 2026, secondary); over 1M daily users |
| 4 | OpenAI Codex | 5M weekly users (Jun 2026); 16.4M extension installs |
| 5 | Gemini CLI, now Antigravity CLI | 108.7k stars; replaced by Antigravity CLI for most users on 2026-06-18 |
| 6 | OpenCode | 224k stars; 1.3M extension installs |
| 7 | Cline | 5.8M installs; 71k stars |
| 8 | Windsurf, now Devin Desktop | "1M+ active users" (**unverified**) |
| 9 | JetBrains AI Assistant and Junie | about 9% workplace use (JetBrains 2026, secondary) |
| 10 | AWS Kiro | 250k users in its first 3 months; replaced Amazon Q Developer |
| 11 | Continue | 4.5M installs, no longer maintained |
| 12 | Roo Code | 2.1M installs, shut down |
| 13 | Kilo Code | 1.7M installs |
| 14 | Goose | 56k stars |
| 15 | Aider | 50k stars, no release since 2025 |

Cloud-only agents (Devin, Jules, the Copilot coding agent, Codex cloud tasks, Cursor background agents) are out of scope: Pigeon connects sessions on the user's computer.

## Changes since the earlier research

- **Gemini CLI was folded into Antigravity CLI.** From 2026-06-18 Gemini CLI stopped serving free, Pro and Ultra users, who moved to the closed-source Antigravity CLI; it remains for Code Assist Standard and Enterprise organizations and paid API keys. Antigravity CLI keeps "Agent Skills, Hooks, Subagents, and Extensions (now as Antigravity plugins)", with `plugin.json`, `mcp_config.json` and `hooks.json` like the Antigravity App ([Google](https://developers.googleblog.com/en/an-important-update-transitioning-gemini-cli-to-antigravity-cli/), [Antigravity CLI plugins](https://antigravity.google/docs/cli/plugins)).
- **Windsurf is now Devin Desktop** (2026-06-02, same product and plans; [FAQ](https://docs.devin.ai/desktop/devin-desktop-faq.md)).
- **Roo Code was shut down** and its repository archived on 2026-05-15; **Continue** is no longer maintained.
- **Several hosts load Claude-format plugins and hooks**: VS Code agent plugins, Devin Desktop, Copilot CLI, Factory Droid and Augment. That makes porting cheap, but it also means the Claude plugin's hooks may already run in those hosts, as they do in Cursor. The Claude hook waiter must stay harmless there (it finds no Claude session and exits).

## Approved hosts

### Antigravity App: A (probed)

Covered in [host integrations research](host-integrations-research.md#antigravity-probe-results-2026-11-01): a sidecar's `agentapi send-message` wakes an idle conversation, and the adapter must track turns through hooks and send only when idle.

### Antigravity CLI (in place of Gemini CLI, decided 2026-11-03): B until probed

Same plugin format as the App, so one Pigeon plugin may serve both. Whether the CLI runs sidecars or accepts `agentapi send-message` is **unverified**; without it the CLI gets restore and turn-boundary delivery.

### OpenCode: A likely

- The TUI is a client of OpenCode's own local server, which has `POST /session/:id/prompt_async` ("Send a message asynchronously") and `/tui/submit-prompt` ([server docs](https://opencode.ai/docs/server/)).
- A JS/TS plugin runs in-process, receives an SDK `client` for that server and events such as `session.idle` and `session.created`, and a plugin tool gets `context.sessionID` ([plugins](https://opencode.ai/docs/plugins/), [custom tools](https://opencode.ai/docs/custom-tools/)).
- So the adapter is a small plugin next to `pigeon mcp`: bind the session from the plugin, and on a wake call `promptAsync` on that session once `session.idle` says it is idle. One process can host several sessions, so events must be filtered by session ID.
- The MCP server gets no session ID; MCP is configured under `mcp` in `opencode.json`. Native Windows support for the CLI is **unverified**.
- Prior art: [hcom](https://github.com/aannoo/hcom) already wakes OpenCode this way.

### GitHub Copilot in VS Code: B, with an A candidate

- VS Code agent plugins accept the Claude format ("VS Code also supports existing Copilot, Claude, and legacy OpenPlugin formats", [agent plugins](https://code.visualstudio.com/docs/agent-customization/agent-plugins)), including skills, MCP and hooks.
- Hooks: `SessionStart`, `UserPromptSubmit`, `Stop` and others, synchronous with a 30 s default; input includes an optional `session_id`; `SessionStart` can return `additionalContext` and `Stop` can block ([hooks reference](https://code.visualstudio.com/docs/agents/reference/hooks-reference)). No async hooks, so `asyncRewake` will not work.
- Wake candidate: a companion VS Code extension calling `workbench.action.chat.open` with a query submits it, but to the last-focused chat, with no way to name a session. Whether that chat is the one Pigeon bound is **unverified** and needs a probe.

### Devin Desktop (Windsurf): B

- Plugins use `.devin-plugin/plugin.json` and fall back to `.claude-plugin/plugin.json`; they carry `.mcp.json`, `hooks.json` and skills.
- Hooks fire in "the Devin CLI, Devin Desktop, and cloud sessions"; every event has a stable `session_id`; `SessionStart` and `UserPromptSubmit` return `additionalContext`; `Stop` can block ([lifecycle hooks](https://docs.devin.ai/cli/extensibility/hooks/lifecycle-hooks.md)). No async hooks.
- No documented idle wake. Whether a headless `devin -r <id> -p` turn appears in the open Desktop chat is **unverified**.

### Cline: B, with an A candidate

- Hooks (`TaskStart`, `UserPromptSubmit` and others) carry `taskId`; their docs now redirect to SDK plugins, so the event list is **unverified**.
- Wake candidates, both **unverified**: a companion extension calling the exported `ClineAPI.sendMessage` on the current task, or the SDK Hub's local WebSocket.
- Cline 4.x is moving to a new SDK runtime (4.0 rolled back, 4.1 ships both), so the adapter should wait for the probe and target the runtime that wins.

### Cursor: B (probe pending)

Unchanged from the earlier research.

## Deferred hosts

| Host | Verdict | Why later |
|---|---|---|
| Kilo Code | A likely | OpenCode-based; a plugin's `client` can `prompt_async` its own session. Smaller audience; nearly free after OpenCode. |
| Sourcegraph Amp | A likely | Plugin `appendUserMessage` on a thread; idle behavior **unverified**; plugins behind a flag. |
| GitHub Copilot CLI | B | Reads Claude-style plugins; synchronous hooks only; `/remote` steering goes through GitHub.com. |
| Factory Droid | B | Translates Claude plugins on install; no idle wake. |
| Charm Crush | B, maybe A | Client-server mode shares live sessions; whether an outside prompt reaches the user's TUI is **unverified**. |
| Qwen Code | B | Revisit when `qwen --serve` runs inside the TUI or async hook output is delivered. |
| AWS Kiro | B | Hooks carry `session_id`; wake only through ACP for sessions Pigeon owns. |
| JetBrains Junie | B | Claude-like hooks, but async output waits for the next prompt; no hooks under ACP. |
| Goose | B | No context-injection output from hooks. |
| Augment (Auggie, IDE) | B weak / C | Session hooks run in the background and cannot inject context. |
| Gemini CLI | B | Only for Code Assist Standard/Enterprise and API-key users. |
| Copilot in Visual Studio, Zed, Warp, Trae, JetBrains AI Assistant | C | No hooks or session ID. Claude Code and Codex running inside Zed or Warp already work. |
| Aider | C | No MCP or hooks; inactive. |
| Roo Code, Continue | dropped | Discontinued. |

## Agent Client Protocol

[ACP](https://agentclientprotocol.com/protocol/prompt-turn) gives one client one agent over stdio, and only the client starts turns (`session/prompt`). Pigeon acting as an ACP client could wake any ACP agent (Claude, Codex, Kiro, Junie and others), but only for sessions Pigeon itself starts and shows. When the user works in Zed or a JetBrains IDE, the editor owns the connection; the only way in would be an ACP proxy between editor and agent, which is still a draft. Not for 0.6.0.

## Proposed build order for alpha.2

1. **Antigravity App**, the only host already probed to A.
2. **OpenCode**, the clearest documented A path.
3. **Copilot in VS Code** and **Devin Desktop** as B, starting from the Claude plugin, then probe the Copilot companion-extension wake.
4. **Cursor** and **Antigravity CLI** as B, with their probes.
5. **Cline** last, after a probe of `ClineAPI.sendMessage`, because its runtime is changing.

Each needs a host test with the user before the PR. Hosts that stay B are documented as delivering at session start and turn end, not unattended.
