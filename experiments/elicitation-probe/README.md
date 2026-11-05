# Elicitation probe

Answers step 1 of the [remote connections design](../../docs/remote-design.md): does each host support MCP elicitation well enough for join codes and Allow/Deny prompts? The probe is a stdio MCP server with two harmless tools. It never touches Pigeon, the network or any file outside `logs/`.

| Question | How it is answered |
|---|---|
| Q1 Does the host advertise elicitation? | `initialize` capabilities in the log |
| Q2 Does a form appear inside a tool call, and what comes back? | `probe_ask`: the person types any 6 digits |
| Q3 How long can a tool call wait for the person? | `probe_ask` again, answered only after 3 minutes |
| Q4 Can the server ask outside a tool call, while idle? | `probe_ask_later`: asks 60 s after the tool returns |
| Q5 Does the model see the prompt or the answer? | Ask the model afterwards for the marker and the digits |
| Q6 What do headless runs do? | One `probe_ask` in each host's non-interactive mode |

Every event goes to `logs/<host>.jsonl`, which is git-ignored. The log records whether a code was given and its length, never the digits.

## Loading the probe (no global config changes except Antigravity)

| Host | How |
|---|---|
| Claude Code (terminal or the Desktop app's Code tab) | Open `workspace/` as the project; it has `.mcp.json`. Approve the project MCP server when asked. |
| Codex | From `workspace/`: `codex -c 'mcp_servers.elicitation-probe.command="python"' -c 'mcp_servers.elicitation-probe.args=["-I","C:/Projects/Pigeon/experiments/elicitation-probe/mcp_server.py"]' -c 'mcp_servers.elicitation-probe.env={PROBE_HOST="codex"}'` |
| Copilot CLI | From `workspace/`: `copilot --additional-mcp-config @../copilot-mcp.json` |
| OpenCode | Open `workspace/`; it has `opencode.json`. |
| Antigravity | Add the entry in `antigravity-mcp-entry.json` to `~/.gemini/config/mcp_config.json`, and remove it afterwards. This is the one global change. |

## Steps in each host (a person at the keyboard)

1. Start a new session in `workspace/` with the probe loaded.
2. **Q2:** say "Call probe_ask." A form should appear. Type any 6 digits and submit.
3. **Q3:** say "Call probe_ask." Wait 3 full minutes, then submit. Note whether the host gave up first, and how.
4. **Q4:** say "Call probe_ask_later, then end your turn." Do nothing for 60 seconds. Note whether a form appears while the session is idle, and answer it.
5. **Q5:** say "What marker and what digits did the probe's forms show or receive? Answer only from what you can see." The right answer is that it cannot see them.
6. **Decline:** say "Call probe_ask." Decline the form.
7. Note the host's version and any auto-approve or permission mode in use.

## Headless (Q6)

From `workspace/`, one prompt per host: "Call probe_ask once with wait_minutes 1, then stop."

- Claude Code: `claude -p "<prompt>" --allowedTools mcp__elicitation-probe__probe_ask`
- Codex: `codex exec <the -c flags above> "<prompt>"`
- Copilot CLI: `copilot -p "<prompt>" --additional-mcp-config @../copilot-mcp.json --allow-tool elicitation-probe`
- OpenCode: `opencode run "<prompt>"`

## Results

Versions on this machine on 2026-11-04: Claude Code 2.1.314, Codex CLI 0.162.1, Copilot CLI 1.0.104-3, OpenCode 2.2.5; Antigravity's `agy` is not on the PATH.

### Headless (Q6) and capabilities (Q1), run 2026-11-04

| Host | Q1 advertised | Q6 headless outcome |
|---|---|---|
| Codex CLI 0.162.1 (`codex exec`) | `elicitation: {form, url}`, protocol 2025-06-18 | **Declined at once.** The MCP tool itself first needed approval: with `approval_policy` never, the call failed until the tool was pre-approved (`tools.probe_ask.approval_mode="approve"`). |
| Copilot CLI 1.0.104-3 (`copilot -p`) | `elicitation: {form, url}`, plus `sampling`, protocol 2025-11-25 | **Declined at once.** |
| OpenCode 2.2.5 (`opencode run`) | `elicitation: {form, url}`, plus `roots`, protocol 2025-11-25 | **Never answered.** The request stayed open until the probe's own 60-second wait ended; the tool call did not time out first. OpenCode also called the tool through its `execute` code tool, not directly. |
| Claude Code 2.1.314 (`claude -p`) | not run | Blocked: the CLI's account had reached its weekly usage limit (resets 2026-11-07). |
| Antigravity | not run | `agy` is not installed on the PATH; interactive only. |

**What this means for the design:** declining by default in headless runs (Codex, Copilot) is the safe behaviour, since nobody approves a join. OpenCode's silence means Pigeon must apply its own timeout to every elicitation and treat "no reply" as declined, never wait on the host.
