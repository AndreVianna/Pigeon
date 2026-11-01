# Pigeon on Antigravity and Cursor: research

Research of 2026-10-31, updated on 2026-11-01 for the two-member topics of 0.5.0-beta.1, for review. It covers what a Pigeon plugin for Google Antigravity and for Cursor would look like, using what we learned building the Claude Code and Codex plugins. The sources are the hosts' official docs and changelogs, checked on this date, plus read-only inspection of the versions installed on this machine at the time (Antigravity App 2.23.0, Cursor 3.24.12). Both were updated on 2026-11-01 to Antigravity 2.28.2, whose changelog adds `SessionStart` hooks and passes the user message to hooks, and Cursor 3.27.22, whose changelog since 2026-10-09 shows no change to hooks or to waking a local chat. Points marked **unverified** need a host test before we rely on them. Nothing has been built or installed.

## What a Pigeon integration needs

From the Claude and Codex plugins, a host integration has six parts:

1. **A plugin package** with a manifest, distributed from this repository's root marketplace.
2. **The MCP server** (`pigeon mcp`) started by the host.
3. **A `connect` skill** that tells the agent how to join a topic.
4. **The session ID**, supplied by the host (hook payload, MCP metadata or environment) and never guessed.
5. **Restore hooks** that rebind a known session at startup or on a prompt.
6. **A wake path**: a supported way for Pigeon to start a turn in an idle conversation. Claude uses an `asyncRewake` hook; Codex uses `codex queue` from the MCP server.

## Summary

| | Claude Code (built) | Codex (built) | Antigravity | Cursor |
|---|---|---|---|---|
| Manifest | `.claude-plugin/plugin.json` | `.codex-plugin/plugin.json` | `.antigravity-plugin/plugin.json` (or root `plugin.json`) | `.cursor-plugin/plugin.json` |
| MCP file | `.mcp.json` | `.mcp.json` | `mcp_config.json` | `mcp.json` |
| Skill | `skills/connect/SKILL.md` | same | same, plus optional `rules/AGENTS.md` | same |
| Hooks file | `hooks/hooks.json` | `hooks/hooks.json` | `hooks.json` | `hooks/hooks.json` (`version: 1`) |
| Hook types | command, `mcp_tool`, async | command, `mcp_tool` | command only, synchronous, 30 s default | command or prompt, synchronous |
| Session ID | hook payload, env | hook payload | hook stdin `conversationId`; MCP `_meta` (unverified in the App) | hook stdin `conversation_id` |
| Idle wake | async hook exit 2 | `codex queue` | **`agentapi send-message`** from a sidecar (documented; idle behavior unverified) | **none documented** |
| Marketplace | `.claude-plugin/marketplace.json` | `.agents/plugins/marketplace.json` | GitHub URL or local path (`agy plugin install`); official Marketplace by intake form | `.cursor-plugin/marketplace.json` |

All four hosts use the same skill format, and Antigravity and Cursor both support a provider-specific manifest folder next to `.claude-plugin/` precisely so one repository can serve several hosts. Pigeon's layout (one folder per host under `integrations/`, root marketplace files) extends naturally, and the lockstep version rule applies to new plugins too.

## Antigravity

**Plugin.** `integrations/antigravity-plugin/` with `.antigravity-plugin/plugin.json` (`name`, `displayName`, `description`, `version`, `logo`, `author`; SemVer version used for update detection), `mcp_config.json` (`{"mcpServers":{"pigeon":{"command":"pigeon","args":["mcp"]}}}`), `skills/connect/SKILL.md` and `hooks.json`. Installed with `agy plugin install <github-url|path>` into `.agents/plugins/` (workspace) or `~/.gemini/config/plugins/` (global); validated with `agy plugin validate --strict`. Plugin MCP servers are namespaced `<plugin>_<server>`, and MCP tool calls ask for approval until the user grants `mcp(pigeon/*)`.

**Session ID.** Every hook receives `conversationId` on stdin. CLI 1.4.4 also sends `antigravity.google/root_conversation_id` in MCP `tools/call` metadata, and the App binary contains a `conversation_id` key next to its MCP code (**unverified** that the App sends it). If it does, the MCP server can bind the session without a restore hook.

**Restore.** Hooks are command-only (no `mcp_tool`), so a `SessionStart`/`PreInvocation` hook would run a new CLI command such as `pigeon hook session-start antigravity`, reading `conversationId` from stdin. `SessionStart` is in the current docs but not in the 2.23 build installed here.

**Wake.** Antigravity has the strongest documented path of the two. A **sidecar** is a background process the app starts and restarts, declared in `sidecars/<id>/sidecar.json` inside a plugin. Sidecars get `agentapi`, whose documented `send-message <conversation_id> <prompt>` "sends a message to an existing conversation". A `pigeon-wake` sidecar could watch the service and send the fixed Pigeon wake prompt, mirroring Codex's queue adapter. Caveats:

- Sidecars are **disabled by default**; the user enables each one in `~/.gemini/config/config.json` (`"<plugin>/<sidecar>": {"enabled": true}`).
- The docs do not say whether `send-message` starts a turn in an idle conversation, or what happens when it is busy (the binary has `WHEN_IDLE` and `NEXT_INVOCATION` delivery strategies, suggesting queuing). **Unverified.**
- A 2026-09-24 forum reply (author's Google affiliation unknown) said external bridges driving sessions on a personal subscription are "not permitted", and that sidecars must run inside the app. Keeping the wake inside a plugin sidecar is the safe reading, but the terms should be checked before release.

**Not possible:** an async background hook; waking a conversation while Antigravity is not running.

## Cursor

**Plugin.** `integrations/cursor-plugin/` with `.cursor-plugin/plugin.json` (only `name` required), `mcp.json` (`{"mcpServers":{"pigeon":{"type":"stdio","command":"pigeon","args":["mcp"]}}}`), `skills/connect/SKILL.md` and `hooks/hooks.json`. Distributed through `.cursor-plugin/marketplace.json` at the repository root (`name`, `owner`, `plugins[]`), or installed locally under `~/.cursor/plugins/local/`. Cursor expands `${CURSOR_PLUGIN_ROOT}` and also `${CLAUDE_PLUGIN_ROOT}`.

**Session ID.** Every hook receives `conversation_id` ("stable ID of the conversation across many turns"). MCP servers get no documented ID, and one server process probably serves all chats in a window, so binding has to come from a hook. The installed bundle sets `CURSOR_CONVERSATION_ID` for the agent's shell commands (**unverified**, undocumented).

**Restore.** Command hooks only, like Antigravity: `sessionStart` and `beforeSubmitPrompt` would run `pigeon hook session-start cursor`. `sessionStart` can also return `additional_context` that tells the agent its conversation ID for `pigeon_session_join`.

**Wake.** No documented way exists for an external process to start a turn in an idle Cursor chat. The options all fall short:

- A `stop` hook can return `followup_message`, which Cursor submits as the next user message (default `loop_limit` 5). A Pigeon stop hook could wait for a message and return it as a follow-up, but hooks are synchronous: the chat shows as running while it waits, the timeout is undocumented (a forum report says about 30–40 s), and a May 2026 forum thread reports follow-ups not landing. It is a long-poll at the end of a turn, not an idle wake.
- `agent --resume <chatId> -p "..."` runs a headless turn in a CLI chat; whether IDE chat IDs work, and whether the turn appears in the IDE, is **unverified**.
- Cloud Agents API, deeplinks (which "never trigger automatic execution") and the new remote control do not reach local IDE chats.

So a Cursor integration would give messaging and restore, with delivery at the start of a session or the end of a turn through hook-delivered wakes (see item 4 below), but no unattended wake.

**Watch out:** Cursor's third-party import (on by default) loads Claude hooks from `.claude/settings*.json` and skills from `.claude/skills`. Whether it also loads installed Claude plugins is unverified; if it does, a Cursor user with the Claude Pigeon plugin could get Claude's hooks running in Cursor.

## Pigeon changes these would need

0.5.0-beta.1 made topics two-member conversations and reduced the agent tools to `pigeon_join`, `pigeon_leave`, `pigeon_topics`, `pigeon_send`, `pigeon_receive` and `pigeon_wake_complete` (plus `pigeon_session_restore` for hooks). That fits both hosts unchanged: each Antigravity or Cursor session joins its pair topics like any other session, and `<tool>` is already free text. What is missing is how each host identifies the session and how it is woken:

1. **Restore from the host's hook payload.** `pigeon hook session-start <tool>` accepts only `codex` and `claude-code` and reads `session_id`. It needs to accept `antigravity` and `cursor`; Cursor already sends `session_id` (equal to `conversation_id`), while Antigravity sends `conversationId`. Neither host has `mcp_tool` hooks, so this command hook is their only restore path.
2. **Session ID from MCP metadata (Antigravity).** If the App sends `antigravity.google/conversation_id` in `tools/call` `_meta`, `pigeon mcp` can bind the session from it, and the connect skill does not need a shell lookup.
3. **A wake mode per host.** The service accepts only `codex-queue` and `claude-hook` wakes and checks them against the session's tool. Antigravity needs a new mode (for example `antigravity-agentapi`) claimed by a sidecar that sends the fixed wake instruction with `agentapi send-message`, then records the transport result, exactly like the Codex queue adapter.
4. **Reading without a wake (Cursor, and any host that cannot be woken).** In 0.5 an agent reads only through `pigeon_receive(generation)`, so a session that is never woken has no way to read its messages. A hook-delivered wake closes the gap without a new tool: Cursor's `sessionStart` hook can return `additional_context`, and its `stop` hook can return `followup_message`. Either can carry the fixed wake instruction with a freshly claimed generation (a new mode such as `cursor-hook`), so the agent drains its messages at the start of a session or at the end of a turn. This gives prompt-time delivery, not an unattended wake.
5. **Plugins** under `integrations/antigravity-plugin` and `integrations/cursor-plugin`, listed in the repository's marketplaces, with the lockstep version.

## Open questions to test on the host

Antigravity:
1. Does the App send a conversation ID in MCP `_meta`?
2. Does `agentapi send-message` start a turn in an idle open conversation, and what does it do when the conversation is busy, closed or archived?
3. Is the hook `conversationId` the ID `send-message` accepts?
4. Does a plugin sidecar start after enabling, and does it get `agentapi` and the language-server address?
5. Does `SessionStart` fire after updating past 2.23, and on resume?

Cursor:
1. What is the real stop-hook timeout, and how does the chat look while the hook waits?
2. Can `agent --resume` target an IDE chat, and does the turn show in the IDE?
3. Is there one MCP server process per window or per chat?
4. Does third-party import load the Claude Pigeon plugin's hooks?

## Proposed Antigravity probe

A short host probe answers the five Antigravity questions before any Pigeon code is written. It uses a throwaway probe plugin, not Pigeon, so nothing in Pigeon changes and no real conversation receives a Pigeon message.

**Probe kit** ([`experiments/antigravity-probe`](../experiments/antigravity-probe/README.md), built and checked locally; plugin sidecars load only from the global plugin folder, so the sidecar is installed on its own and the plugin only in a test workspace):
- An MCP server (a small Python script) that logs each `initialize` and `tools/call` request's `_meta` keys and the names (not values) of `ANTIGRAVITY_*` environment variables, and offers one harmless tool, `probe_echo`.
- Command hooks for `SessionStart`, `PreInvocation` and `Stop` that log the event name, `conversationId` and the payload's field names.
- A sidecar that logs the same environment variable names and, when a trigger file names a conversation ID, runs `agentapi send-message <id> "Pigeon probe: reply with PONG."` and logs the exit code and output.
- All logs go to one folder in the kit, so they can be read and deleted afterwards.

**What needs your approval and your hands:**
1. Installing the probe plugin in a new, empty Antigravity workspace (workspace scope, `.agents/plugins/`), so no other workspace loads it.
2. Enabling its sidecar in `~/.gemini/config/config.json`, which is Antigravity's global configuration; it is removed again afterwards.
3. Restarting Antigravity and opening one new test conversation in that workspace.
4. Optionally updating Antigravity past 2.23, to test `SessionStart`.

**Tests**, each in that one conversation:
1. Call `probe_echo` once: does the MCP log show a conversation ID in `_meta`?
2. Compare the hook's `conversationId` with the `_meta` value.
3. With the conversation idle and open, trigger the sidecar's `send-message`: does a turn start by itself? Then repeat while a turn is running, after closing the conversation, and after archiving it.
4. Check the sidecar log for `agentapi` and the language-server address.
5. Note whether `SessionStart` fired, and whether it fires again when the conversation is reopened.

About 30 minutes. Cleanup: disable the sidecar, remove the probe plugin and workspace, delete the logs.

Before building the Antigravity wake adapter, the terms-of-service question in the Antigravity section should be settled.

## Antigravity probe results (2026-11-01)

The probe answered the open questions on Antigravity 2.28.2 (details in [the probe runbook](../experiments/antigravity-probe/README.md#results-2026-11-01-antigravity-2282)):

- **Identity:** the conversation ID arrives in every MCP `tools/call` (`_meta`), in every hook, and is the ID `agentapi send-message` accepts. `pigeon mcp` can bind its session from the first tool call; `SessionStart` fires for each new conversation and can restore it.
- **Wake works when idle:** a sidecar's `language_server.exe agentapi send-message <id> <text>` starts a turn in an idle conversation, also while a background command runs.
- **Never send during a turn:** a message sent mid-turn joins the running turn, and the agent dropped the user's task to investigate it. `agentapi` exposes no busy state, so the adapter must track turns from the plugin's hooks (`PreInvocation` starts one, `Stop` with `fullyIdle` ends it) and send only when the conversation is idle.
- **Acceptance is not delivery:** a message to a deleted conversation is accepted and lost. Pigeon's existing wake retries and limits cover this.
- **The wake text must stand on its own:** an unexplained system message made the agent explore files to find its source. The wake instruction should name Pigeon and the tool to call, and the plugin's rule or skill should explain it.
- **Approvals:** each Pigeon tool call prompts until allowed; unattended delivery needs `mcp(pigeon/*)` allowed for the project or always.

This confirms Antigravity can reach parity with Claude and Codex, with one addition to the adapter: idle tracking through hooks.

## Cursor probe results so far (2026-11-01)

A [Cursor probe](../experiments/cursor-probe/README.md) is built; its run stopped at the account's agent usage limit, so the MCP, `sessionStart` context and stop-hook questions are still open. It did show:

- **Cursor runs the Claude Pigeon plugin's hooks.** Cursor imports installed Claude plugins' hooks and runs them through PowerShell on Windows; the plugin's direct `hook-wait.sh` command opened the script in a text editor instead of running it. The Claude plugin should call `bash "${CLAUDE_PLUGIN_ROOT}/scripts/hook-wait.sh"` explicitly so it runs under any shell. In Cursor the waiter would then find no Claude session and exit at once.
- **Cursor's hooks carry the conversation ID** in both `conversation_id` and `session_id`, so the existing `pigeon hook session-start` command could read Cursor's ID from `session_id` without changes to its input parsing.

## Recommendation

Do **Antigravity first**: it has a documented route to a real wake (sidecar plus `agentapi send-message`), so it can reach parity with Claude and Codex if the idle-turn test passes. Start with a short host probe of the five Antigravity questions before writing the adapter, and confirm the terms-of-service position.

Treat **Cursor as messaging without unattended wake** until Cursor documents a way to start a turn in an idle chat. Its plugin is cheap (manifest, MCP, skill, restore hooks) and useful for prompt-time delivery, but should be described honestly as not waking.

Both come after the 0.5.0 release, since each needs service and CLI changes (items 1–4 above). Antigravity needs items 1–3; Cursor needs items 1 and 4.

## Sources

Antigravity:
- https://antigravity.google/docs/build-plugins
- https://antigravity.google/docs/plugins
- https://antigravity.google/docs/hooks
- https://antigravity.google/docs/mcp
- https://antigravity.google/docs/sidecars
- https://antigravity.google/docs/permissions
- https://antigravity.google/docs/rules
- https://antigravity.google/docs/cli/reference
- https://antigravity.google/docs/changelog (App 2.28.2 and CLI 1.4.10, 2026-10-29)
- https://discuss.ai.google.dev/t/tos-clarification-may-a-local-codex-mcp-bridge-use-agentapi-with-antigravity/184590 (2026-09-24)
- Local: the docs bundled with App 2.23.0 under `~/.gemini/antigravity/builtin/skills/agy-customizations/`

Cursor:
- https://cursor.com/docs/plugins
- https://cursor.com/docs/reference/plugins
- https://cursor.com/docs/agent/hooks
- https://cursor.com/docs/reference/third-party-hooks
- https://cursor.com/docs/context/mcp
- https://cursor.com/docs/context/skills
- https://cursor.com/docs/integrations/deeplinks
- https://cursor.com/docs/cli/overview
- https://cursor.com/docs/cli/reference/parameters
- https://cursor.com/docs/cloud-agent/api/endpoints
- https://cursor.com/changelog (2026-10-06 and 2026-10-20 entries)
- https://forum.cursor.com/t/stop-hook-followups-fail-to-land-after-waiting-for-background-tasks/161164 (May 2026)
- https://forum.cursor.com/t/allow-granular-import-of-third-party-plugins-skills-etc/152279
- Local: the plugin cache of Cursor 3.24.12 under `~/.cursor/plugins/cache/`
