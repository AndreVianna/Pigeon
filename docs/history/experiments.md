# Experiments (removed)

The `experiments/` folder held probe kits and development copies of the plugins for the live host tests. It was never product code. It was removed in two steps; the findings and test results are kept here and in the design documents.

## Removed on 2026-11-25

The probes had answered their questions and the development plugin copies were frozen at 0.5.0-beta.2 and 0.6.0-alpha.2. Nothing in the product, the tests or CI used them. The code is in Git history; commit `111ebbd` is the last that contains it.

| Was | What it was | Kept write-up |
|---|---|---|
| `host-test/` | Runbook, helper (`hosttest.py`) and results of the release host tests: H1–H6, E1–E6, A1–A8, O1–O6, C1–C6, M1–M7 and D1–D7 | [host-tests.md](host-tests.md) |
| `antigravity-probe/` | Probe plugin, sidecar and helper for the open Antigravity questions (run on 2026-11-01) | [antigravity-probe.md](antigravity-probe.md), [research](../host-integrations-research.md) |
| `copilot-probe/` | Probe MCP server, hooks and extension for the open GitHub Copilot questions (run on 2026-11-04) | [copilot-probe.md](copilot-probe.md), [research](../host-adapters-research.md) |
| `elicitation-probe/` | Probe MCP server and per-host configs for MCP elicitation in each host (run on 2026-11-04) | [elicitation-probe.md](elicitation-probe.md), [design](../remote-design.md) |
| `cursor-probe/` | Probe project config and hooks for the open Cursor questions (partly run on 2026-11-01; stopped at the account usage limit, so its questions are still open) | [cursor-probe.md](cursor-probe.md), [research](../host-integrations-research.md) |
| `claude-hook-waiter-dev/` | Development Claude plugin: production hooks with an isolated port, the frozen test build and a short renewal deadline | [claude-hook-waiter-dev.md](claude-hook-waiter-dev.md) |
| `codex-plugin-dev/`, `antigravity-dev/` | Development Codex and Antigravity plugins: isolated port and the frozen test build | [host-tests.md](host-tests.md) |
| `copilot-dev/`, `opencode-dev/` | Generators (`make_dev.py`) and generated development copies of the Copilot and OpenCode plugins | [host-tests.md](host-tests.md) |

A new host test needs a development copy made from the current plugin in `integrations/`, with its own port (`PIGEON_PORT`) and database (`PIGEON_DATABASE`). The old copies would have had to be regenerated anyway.

## Removed on 2026-10-31

Earlier probes were removed because they targeted earlier Pigeon versions. Their findings are in this folder and in [session recovery](../session-recovery.md); the Claude resume probe's report is [claude-resume-probe-report.md](claude-resume-probe-report.md). Commit `6b25fbb` is the last that contains the code and logs:

- `claude-channel/`, `pigeon_claude_wake.py`: Claude Code development-channel wake (alpha.13).
- `claude-resume-probe/`: hook and plugin monitor recovery in Claude Desktop (code and run logs).
- `claude-desktop-diagnostic/`: skill-invoked plugin monitor in Desktop.
- `claude_plugin_monitor_probe.py`, `session_hook_probe.py`: plugin monitor and startup/prompt hooks in nonpersistent sessions.
- `codex_*.py`, `pigeon_codex_wake.py`: Codex app-server queue, restart and MCP session environment.
- `polling_e2e.py`, `http_mcp_probe.py`, `loopback_probe.py`: early polling and loopback connectivity.
- `claude-hook-waiter-dev/hook-wait.ps1`: unpackaged PowerShell wrapper (blocked by execution policy), and the dev plugin's earlier hook files.
