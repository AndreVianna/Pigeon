# Copilot probe: runbook

A throwaway probe for the open GitHub Copilot questions in [the host adapters research](../../docs/host-adapters-research.md#github-copilot-in-vs-code-b-with-an-a-candidate), run before any Pigeon code is written. It does not use Pigeon. Prepared and run on 2026-11-04 with VS Code 1.145.0 (results below); the GitHub Copilot app step was not run.

VS Code now has two chat harnesses. **Copilot** is the default for new chats and is shared with the GitHub Copilot app and Copilot CLI. **Local** is the older in-window harness, chosen in the Session Target control. They read different configuration files and send different payloads, so the probe registers its MCP server and hooks for both.

## Results (2026-11-04, VS Code 1.145.0)

Run in `C:\Projects\PigeonCopilotProbe` with two Local chats (`51934ddf-…`, `b7bb5b94-…`) and two Copilot chats that called the tool (`c2f77181-…`, `09da179d-…`); two more Copilot sessions ended without a call.

| # | Answer |
|---|---|
| 1 | **Copilot: no.** `tools/call` `_meta` holds only `progressToken`. But the Copilot runtime (`copilot-runtime.exe`, MCP client `copilot-cli` 1.0.103) starts **one MCP server process per chat session**, and that process's environment has **`COPILOT_AGENT_SESSION_ID`**, equal to the session's hook `sessionId` (`09da179d-…` in both). |
| 2 | **Local: yes.** `_meta` has `vscode.conversationId` (with `vscode.requestId`, `progressToken`, `traceparent`); one MCP server process (client `Visual Studio Code` 1.145.0) serves every Local chat. |
| 3 | **Yes in both.** Local: `vscode.conversationId` equals the hooks' `session_id`. Copilot: `COPILOT_AGENT_SESSION_ID` equals the hooks' `sessionId`. |
| 4 | Local: `SessionStart`, `UserPromptSubmit`, `Stop` (snake_case fields, `session_id`). Copilot: `sessionStart`, `userPromptSubmitted`, **`agentStop`** and `sessionEnd` (camelCase, `sessionId`), and it also ran the PascalCase entries with snake_case fields. So **`agentStop` runs in VS Code's Copilot harness**. |
| 5 | Local: one shared process. Copilot: one process per session, started when the session is created and gone when it ends. |
| 6 | Not run. |

**Wake (2026-11-04, VS Code 1.145.0, Copilot harness).** A probe Copilot extension (`workspace/.github/extensions/pigeon-probe/extension.mjs`) loaded with no setting changed: the runtime forked it per session with `SESSION_ID` set to the chat's session ID (`d5a126d5-…`; `COPILOT_AGENT_SESSION_ID` is not set for extensions), `joinSession()` attached to that session, and `session.on` delivered event types including `assistant.turn_start`, `assistant.turn_end`, `session.idle` and `pending_messages.modified`. With the chat idle, a trigger file made the extension call `session.send({prompt, mode: "enqueue"})` at 16:23:48 UTC; a `user.message` and a new turn followed at 16:23:49 with nobody typing, the chat showed "Pigeon probe: reply with the single word PONG." as a user message, and the agent answered PONG after asking the user's permission for a tool. **So the Copilot harness can wake an idle, named session through an extension.**

**Tool approval (2026-11-04).** In a new Copilot chat, VS Code asks to allow each MCP server's tools ("Allow tool from pigeon-probe?", with only "Allow once" or "Allow in this Session"), so the first wake in every chat would wait for the user. Two fixes were tried:
- A `preToolUse` hook returning `permissionDecision: "allow"` did not work as written, and taught three things. The runtime runs a hook's `command` through PowerShell (not its `powershell` field), so a quoted program path followed by an argument is a PowerShell syntax error; it reads backslash escapes in the command, so `` in `...\scriptsllow.py` became a bell character; and a failing `preToolUse` hook is **fail-closed**, blocking every tool, including the agent's tool search. The runtime (`copilot-runtime.exe`) also outlives a VS Code restart, and each chat reads the hook files when it starts. Forward slashes would avoid the escapes; not retried.
- **The extension's permission handler works.** `joinSession({ onPermissionRequest })` receives each request (`kind: "mcp"`, `serverName: "pigeon-probe"`, `toolName: "pigeon-probe-probe_echo"`, the server name prefixed; `managedSettingsEnabled: false`). Answering `{ kind: "approve-once" }` for only that tool, and `{ kind: "no-result" }` for everything else so the user still decides, let a wake in a new chat run `probe_echo` with no prompt: requested 16:50:25.027, approved 5 ms later, tool called, chat idle at 16:50:26.

## Questions

| # | Question | What answers it |
|---|---|---|
| 1 | Does an MCP `tools/call` in a **Copilot** session carry the chat session ID, and under which `_meta` key? | `mcp.jsonl`: the `meta` of a `tools/call`. |
| 2 | Same in a **Local** session: is it `vscode.conversationId`, as the source says? | The same. |
| 3 | Do the hooks' session IDs (`sessionId` in Copilot, `session_id` in Local) equal the MCP ID? | Compare `hook.jsonl` with `mcp.jsonl`. |
| 4 | Which hooks fire in each harness: `sessionStart`, `userPromptSubmitted`, `agentStop`, `sessionEnd`, and the Local `Stop`? The source suggests VS Code's Copilot harness may not run `agentStop`. | `hook.jsonl` events. |
| 5 | Does one MCP server process serve several chats? | The `pid` of `tools/call` entries from two chats. |
| 6 | The same in the GitHub Copilot app, if it can open this folder. | As above. |

Not covered: waking a chat. The only targeted wake found is the agent host's local protocol, which needs the connection token VS Code writes to disk. Reading it needs the user's explicit approval and a security review, so it is left out of this probe.

## What is in the kit

| Piece | Where | What it does |
|---|---|---|
| MCP server | `scripts/mcp_server.py` | One harmless tool, `probe_echo`. Logs `initialize`, the names (not values) of `VSCODE*`, `COPILOT*`, `GITHUB*` and `ELECTRON*` variables, and each `tools/call`'s `_meta` and argument names. |
| Hooks | `scripts/hook.py` | Logs the event, `sessionId`/`session_id` and a few status fields, and the payload's field names. Never logs prompts, tool arguments or transcripts. Never blocks. |
| Workspace files | `workspace/` | `.mcp.json` (Copilot harness), `.vscode/mcp.json` (Local harness) and `.github/hooks/pigeon-probe.json` (both harnesses; Copilot camelCase and Local PascalCase events). |

Logs go to `logs/` here (ignored by Git). The scripts run with `python` from the PATH and from this repository's path, so the repository must stay at `C:\Projects\Pigeon`.

## Setup (needs your approval)

1. **Test workspace:** create the empty folder `C:\Projects\PigeonCopilotProbe`, make it a Git repository (`git init`; hook files are found at the repository root), and copy the contents of `workspace\` into it.
2. **You:** open that folder in VS Code and trust it. Allow the `pigeon-probe` MCP server when asked.
3. **You:** in a new chat with **Copilot** as the Session Target, ask: *"Call probe_echo with the text one."* Then start a second Copilot chat and ask the same with *"two"*.
4. **You:** switch the Session Target to **Local**, start a new chat, and ask: *"Call probe_echo with the text three."*
5. **You, optional:** open the same folder in the GitHub Copilot app and ask: *"Call probe_echo with the text four."*

About 15 minutes. Then I read the logs and record the answers here and in the research.

## Cleanup

1. Close the folder in VS Code (and the Copilot app), and delete `C:\Projects\PigeonCopilotProbe`.
2. Delete `experiments\copilot-probe\logs` after the results are recorded.
