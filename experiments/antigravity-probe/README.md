# Antigravity probe: runbook

A throwaway probe that answers the open Antigravity questions in [the host integrations research](../../docs/host-integrations-research.md) before any Pigeon code is written. It does not use Pigeon. Built for and run with Antigravity 2.28.2; results below.

## Results (2026-11-01, Antigravity 2.28.2)

Run in two new test workspaces with one conversation each (`e905f235-…`, later deleted with its project, and `df0644c3-…`). The sidecar was enabled in the Automations dashboard by the user.

| # | Question | Result |
|---|---|---|
| 1 | Conversation ID in MCP calls | **Yes.** Every `tools/call` carries `_meta` with `antigravity.google/conversation_id`, `antigravity.google/root_conversation_id` and `antigravity.google/artifacts_dir`. `initialize` and `tools/list` carry none, and the MCP server gets no `ANTIGRAVITY_*` variables. |
| 2 | Same ID in hooks | **Yes.** The hooks' `conversationId` equals the `_meta` conversation ID and is the ID `send-message` accepts. |
| 3a | `send-message` to an idle open conversation | **Wakes it.** Accepted in about 0.3 s; the message appears as "Message from System" and the agent answered PONG with no user action. |
| 3b | While a background command runs, but the turn has ended | **Wakes it.** A new turn started 0.2 s after the message. |
| 3c | While a turn is running | **Hijacks the turn.** The message joins the running turn at its next model call. Twice, the agent abandoned the user's task (writing numbers) and instead investigated the "system message": it checked Python, called `probe_echo`, and read the probe's files outside its workspace (each needing approval), then answered PONG. |
| 3d | To a conversation whose project was deleted | **Silently lost.** `send-message` still exits 0 with a normal response; nothing happens. |
| 4 | Sidecar environment | `ANTIGRAVITY_AGENTAPI_EXE` names `language_server.exe`, which is called as `language_server.exe agentapi send-message <id> <text>`; calling it without `agentapi` crashes it. Sidecars also get `ANTIGRAVITY_LS_ADDRESS`, `ANTIGRAVITY_CSRF_TOKEN`, `ANTIGRAVITY_PROJECT_ID` and others. `agentapi` offers only `send-message [--title]`, `new-conversation` and `get-conversation-metadata`; the metadata has no busy or idle state. |
| 5 | `SessionStart` | **Fires** once per new conversation, with its `conversationId`, about 0.1 s before the first model call. `PreInvocation` fires before each model call and `Stop` at the end of a turn, with `fullyIdle` false while a background task still runs. |

Other observations:
- Hook commands run through `cmd /c`; a command with two quoted paths fails silently, so the hooks use `python <unquoted script path>`. Hooks edits took effect in a running conversation without a restart.
- MCP tools and file reads outside the workspace each prompt for approval ("this time", conversation, project or always). Delivery needs the Pigeon tools allowed, or every wake waits for the user.
- The archived-conversation case was not tested: the first project was deleted, which removed its conversation.

## What is in the kit

| Piece | Where | What it does |
|---|---|---|
| Probe plugin `pigeon-probe` | `plugin/pigeon-probe/` | Workspace plugin: an MCP server with one harmless tool, `probe_echo`, and `SessionStart`, `PreInvocation` and `Stop` hooks. |
| MCP server | `scripts/mcp_server.py` | Logs the client's `initialize`, the names of `ANTIGRAVITY_*` variables it receives, and the `_meta` of each `tools/call`. |
| Hooks | `scripts/hook.py` | Logs the event, `conversationId`, a few status fields and the payload's field names. Never logs the prompt (`lastUserInput`) or the transcript. Always lets the agent continue. |
| Sidecar `pigeon-probe` | `sidecar/pigeon-probe/sidecar.json`, `scripts/sidecar.py` | Logs the variable names it receives and where `agentapi` is, then waits for a trigger. For each trigger it runs `agentapi send-message <id> "Pigeon probe: ... Reply with the single word PONG."` once and logs the exit code and output. |
| Helper | `probe.py` | `trigger <conversation-id>`, `metadata <conversation-id>`, `show` (print the logs), `clean` (delete them). |

All logs go to `logs/` here (ignored by Git). Environment variables are logged by name only, because one of them holds the language server's CSRF token. The scripts run with `C:\Program Files\Python312\python.exe` and from this repository's path, so the repository must stay at `C:\Projects\Pigeon` while the probe runs.

Plugin sidecars load only from Antigravity's global plugin folder, which would put the probe's MCP server and hooks into every workspace. The sidecar is therefore installed on its own, globally, and the plugin only in a test workspace.

## Setup (needs your approval)

1. **Test workspace.** Create the empty folder `C:\Projects\PigeonAntigravityProbe` and copy `plugin\pigeon-probe` into it as `.agents\plugins\pigeon-probe`.
2. **Sidecar.** Copy `sidecar\pigeon-probe` to `%USERPROFILE%\.gemini\config\sidecars\pigeon-probe`. This is Antigravity's global configuration folder.
3. **You:** restart Antigravity, open `C:\Projects\PigeonAntigravityProbe` as a workspace, and enable **Pigeon probe** in the Automations dashboard. Antigravity asks for this to be done by hand.
4. **You:** open one new conversation in that workspace and send a short prompt, for example *"Say hello."*

## Tests

Run the helper from the repository root as `python -I experiments/antigravity-probe/probe.py <command>`.

| # | Question | Steps | What answers it |
|---|---|---|---|
| 1 | Does the App send the conversation ID to MCP servers? | Ask the conversation: *"Call probe_echo with the text hi."* | `mcp.jsonl`: a `tools/call` whose `meta` holds a conversation ID. |
| 2 | Is the hook's `conversationId` the same ID? | Compare `hook.jsonl` with the `_meta` value. | Matching IDs. |
| 3 | Does `agentapi send-message` start a turn in an idle conversation? | With the conversation idle and open: `trigger <conversation-id>`. Repeat while a turn is running, after closing the conversation, and after archiving it. | A new turn that answers PONG, and the `send-message` entries in `sidecar.jsonl` (exit code, output, timing). |
| 4 | Does a sidecar get `agentapi` and the language server address? | Look at the sidecar's `started` entry. | `agentapi` path and the variable names present. |
| 5 | Does `SessionStart` fire, also on reopening? | Check `hook.jsonl` after the first prompt, then close and reopen the conversation and prompt again. | `SessionStart` entries and their `conversationId`. |

About 30 minutes. Record the results in this README and in the research report.

## Cleanup

1. Disable **Pigeon probe** in the Automations dashboard and delete `%USERPROFILE%\.gemini\config\sidecars\pigeon-probe`.
2. Delete `C:\Projects\PigeonAntigravityProbe` and `C:\Projects\PigeonAntigravityProbe2` and archive the test conversations.
3. `python -I experiments/antigravity-probe/probe.py clean`.
