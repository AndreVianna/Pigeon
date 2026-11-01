# Cursor probe: runbook

A throwaway probe that answers the open Cursor questions in [the host integrations research](../../docs/host-integrations-research.md) before any Pigeon code is written. It does not use Pigeon. Built for Cursor 3.27.22; partly run, see below.

## Partial results (2026-11-01, Cursor 3.27.22)

The run stopped early: the Cursor account had reached its agent usage limit, so no model turn ran and questions 1 to 4 remain open. Rerun them when agent usage is available.

| # | Question | Result |
|---|---|---|
| 1–4 | MCP metadata, server per chat, `sessionStart` context, stop-hook follow-ups | **Not answered.** The probe's MCP server never started, because no agent turn ran. |
| 5 | Does Cursor run the installed Claude Pigeon plugin's hooks? | **Yes, and they misbehave.** Cursor imports the hooks of installed Claude plugins and runs hook commands through PowerShell. The Claude plugin's command calls `hook-wait.sh` directly, so Windows opened the script in its associated editor (Notepad++) when a chat started. |

Also seen: `sessionStart` and `beforeSubmitPrompt` fire before the agent runs (even when the model then refuses for the usage limit), and every payload carries both `conversation_id` and `session_id` with the same value, plus `composer_mode`, `is_background_agent`, `generation_id`, `model`, `cursor_version`, `transcript_path`, `workspace_roots` and `user_email`.

## What is in the kit

| Piece | Where | What it does |
|---|---|---|
| Project config | `project/.cursor/mcp.json`, `project/.cursor/hooks.json` | Copied into a test workspace only. A local Cursor plugin would load in every workspace; project config loads only where it is. |
| MCP server | `scripts/mcp_server.py` | One harmless tool, `probe_echo`. Logs `initialize`, the names of `CURSOR*`, `VSCODE*` and `CLAUDE*` variables it receives, the `_meta` of each `tools/call`, and its process and parent IDs. |
| Hooks | `scripts/hook.py` | `sessionStart`, `beforeSubmitPrompt` and `stop`. Each logs the event, `conversation_id`, `generation_id`, status fields and the payload's field names; never the prompt, attachments, transcript or e-mail address. `sessionStart` adds the sentence "the code word for this session is PEGASUS" to the context. `stop` can wait a set time and then return a follow-up asking for PONG. |
| Helper | `probe.py` | `stop <seconds>` (arm the next stop hook), `show`, `clean`. |

Logs go to `logs/` here (ignored by Git). The scripts run with `python` from PATH and from this repository's path, so the repository must stay at `C:\Projects\Pigeon` while the probe runs. The stop hook's timeout is set to 900 seconds to find Cursor's real limit.

## Setup (needs your approval)

1. **Test workspace.** Create the empty folder `C:\Projects\PigeonCursorProbe` and copy `project\.cursor` into it.
2. **You:** open that folder in Cursor, approve the `pigeon-probe` MCP server if Cursor asks, and start one new agent chat.

Nothing global is changed.

## Tests

Run the helper from the repository root as `python -I experiments/cursor-probe/probe.py <command>`.

| # | Question | Steps | What answers it |
|---|---|---|---|
| 1 | Does the conversation ID reach MCP tool calls? | In the chat: *"Call probe_echo with the text hi."* | `mcp.jsonl`: whether `tools/call` has `_meta` with a conversation ID, and which variables the server got. |
| 2 | One MCP server per window or per chat? | Start a second chat in the same window and call `probe_echo` again. | Whether both calls come from the same process ID. |
| 3 | Does the agent see `sessionStart` context? | In a new chat: *"What is the code word for this session?"* | It answers PEGASUS. |
| 4 | How long can a `stop` hook hold a follow-up, and what does the chat show? | `stop 20`, then send any prompt; watch the chat after the reply. Repeat with `stop 90` and `stop 300`. | `stop-wait-start` and `stop-wait-end` entries, whether PONG arrives, and what the chat shows while the hook waits. A missing `stop-wait-end` means Cursor ended the hook first. |
| 5 | Does Cursor run the installed Claude Pigeon plugin's hooks? | In Cursor Settings, open Hooks (and the Hooks Execution Log) and check for the Pigeon `hook-wait.sh` hooks. | Whether they are listed or ran. |

About 30 minutes, mostly the stop-hook waits. Record the results in this README and in the research report.

## Cleanup

1. Close the test workspace in Cursor and delete `C:\Projects\PigeonCursorProbe`.
2. `python -I experiments/cursor-probe/probe.py clean`.
