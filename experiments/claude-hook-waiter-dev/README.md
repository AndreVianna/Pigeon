# Pigeon hook waiter: development plugin

This is a historical record of the milestone 2 fixture. Its comparison with the production plugin describes the state before alpha.18 and is not current setup guidance; see the root README for the current plugin.

Milestone 2 of `docs/claude-hook-waiter-plan.md`. A copy of `integrations/claude-plugin` that exercises the `pigeon hook wait claude-code` waiter without touching the production plugin or the user's Pigeon service.

## What differs from the production plugin

| | Production `pigeon@pigeon-local` | This copy `pigeon-dev@pigeon-hook-waiter-dev` |
|---|---|---|
| Binary | `pigeon` on `PATH` (installed tool) | `src/Pigeon.Cli/bin/Debug/net11.0/pigeon.exe` |
| Service | default port 49372 | isolated port **49473** (`PIGEON_PORT` in `.mcp.json` and every hook command) |
| MCP server name | `plugin:pigeon:pigeon` | `plugin:pigeon-dev:pigeon` |
| `SessionStart` | sync context hook (`pigeon hook session-start`) telling the model to start a Monitor | **async + asyncRewake waiter** (`pigeon hook wait claude-code`), matcher `startup\|resume` |
| `Stop` | none | **async + asyncRewake waiter**, `timeout: 600` (the waiter's own default deadline is 570 s) |
| `UserPromptSubmit` | `pigeon_session_restore` MCP hook | same, on the dev server |
| Connect skill | starts a Monitor | joins the topic and does **not** start a Monitor |

The sync `SessionStart` context hook is left out on purpose: its text asks the model to start a Monitor, which would compete with the waiter. If the production plugin adopts the waiter, `SessionHook`'s context text needs a waiter-aware variant.

## Smoke test (done, 2026-10-31)

A disposable service on port 49473 with a temporary database, the exact `Stop` command string from `hooks.json` run through Git Bash, a `pigeon mcp` bridge with `CLAUDE_CODE_SESSION_ID` set, and one `send-session` message:

- unknown session: exit 0, no output, 0.2 s;
- while waiting, `pigeon status 49473` showed `notification: claude-hook`;
- after the message: exit 2, empty stdout, stderr = the fixed `Pigeon wake generation …` instruction only;
- afterwards: `notification: none (last claude-hook woke at …)`.

No host (Claude Code) was involved; that is milestone 3.

## Using it

How milestone 3 ran it (all steps were approved and are undone; repeat them only with approval):

1. Build Debug and do not rebuild while sessions use it: hooks and the MCP server run that exe. Every connected test conversation keeps a `pigeon.exe mcp` child running from it.
2. Start the isolated service with `PIGEON_PORT=49473` and a temporary `PIGEON_DATABASE`. `pigeon.exe start --foreground` stops when its stdin closes; to survive a Desktop restart, start it with `pigeon.exe start` from outside Desktop's process tree.
3. From a separate folder (milestone 3 used `C:\Projects\PigeonHookWaiterTest`, so the production plugin is not loaded), install at local scope:
   `claude plugin marketplace add "C:\Projects\Pigeon\experiments\claude-hook-waiter-dev" --scope local`
   `claude plugin install pigeon-dev@pigeon-hook-waiter-dev --scope local`
4. Undo: `claude plugin uninstall …`, `claude plugin marketplace remove pigeon-hook-waiter-dev`, delete `~/.claude/plugins/cache/pigeon-hook-waiter-dev`, stop the service and any Debug `pigeon.exe` (waiters and `mcp` children), delete the folder and database.

## Results

Milestone 3 ran 12 end-to-end scenarios in Claude Desktop (bundled Claude Code 2.1.309); see "Milestone 3 results" in `docs/claude-hook-waiter-plan.md`. In short: every one of 21 synthetic messages was acknowledged exactly once; idle wakes, re-arming, busy turns, bursts, service restarts and coexistence with a Monitor worked without a prompt; the gaps are the waiter deadline (a message waits for the next turn) and reopen/app restart (delivery only when Desktop starts the conversation's process).

## Notes

- **Hook shell.** The commands use the `VAR=value cmd` form, which needs Git Bash (present here). Under PowerShell it would fail.
- **Hook settings are fixed at conversation start.** Editing `hooks.json` did not change conversations that had already loaded the plugin; uninstalling removed the waiter from their next Stop hook.
- **Waiter deadline below the hook timeout.** A hook timeout shorter than the waiter's deadline makes Desktop kill the waiter silently (status `lost`).
- **Every turn of every conversation in the folder** runs the waiter once; unconnected sessions exit in about 0.2 s.
