# Experiments

Development fixtures for the live host tests. They are not product code. Every test that starts an agent session, service or plugin needs the user's approval first, and uses new isolated sessions, a disposable database and synthetic messages.

| Path | Purpose | Write-up |
|---|---|---|
| `host-test/` | Runbook, helper and results for release host tests (H1–H3 passed; H4–H6 pending) | [runbook](host-test/README.md), [release plan](../docs/release-plan.md) |
| `claude-hook-waiter-dev/` | Development Claude plugin: production hooks with an isolated port, the frozen test build and a short renewal deadline | [README](claude-hook-waiter-dev/README.md) |
| `codex-plugin-dev/` | Development Codex marketplace and plugin: isolated port and the frozen test build | [runbook](host-test/README.md) |
| `antigravity-probe/` | Probe plugin, sidecar, runbook and results for the open Antigravity questions (run on 2026-11-01) | [runbook](antigravity-probe/README.md), [research](../docs/host-integrations-research.md) |
| `cursor-probe/` | Probe project config, hooks and runbook for the open Cursor questions (partly run on 2026-11-01; blocked by the account usage limit) | [runbook](cursor-probe/README.md), [research](../docs/host-integrations-research.md) |

## Removed probes

Earlier probes were removed on 2026-10-31 because they targeted earlier Pigeon versions and are no longer needed. Their findings are in [docs/history](../docs/history/) and [session recovery](../docs/session-recovery.md); the Claude resume probe's report is now [docs/history/claude-resume-probe-report.md](../docs/history/claude-resume-probe-report.md). The code and logs remain in Git history; commit `6b25fbb` is the last that contains them:

- `claude-channel/`, `pigeon_claude_wake.py`: Claude Code development-channel wake (alpha.13).
- `claude-resume-probe/`: hook and plugin monitor recovery in Claude Desktop (code and run logs).
- `claude-desktop-diagnostic/`: skill-invoked plugin monitor in Desktop.
- `claude_plugin_monitor_probe.py`, `session_hook_probe.py`: plugin monitor and startup/prompt hooks in nonpersistent sessions.
- `codex_*.py`, `pigeon_codex_wake.py`: Codex app-server queue, restart and MCP session environment.
- `polling_e2e.py`, `http_mcp_probe.py`, `loopback_probe.py`: early polling and loopback connectivity.
- `claude-hook-waiter-dev/hook-wait.ps1`: unpackaged PowerShell wrapper (blocked by execution policy), and the dev plugin's earlier hook files.
