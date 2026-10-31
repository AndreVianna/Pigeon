# Experiments

Probes and fixtures used to establish host behavior. They are evidence, not product code: most target earlier Pigeon versions and may not run against the current CLI. Every probe that starts an agent session, service or plugin needs the user's approval first, and uses new isolated sessions, a disposable database and synthetic messages.

| Path | What it established | Write-up |
|---|---|---|
| `claude-hook-waiter-dev/` | Development Claude plugin for the Stop-hook waiter (milestone 3 Desktop tests) | [report](../docs/history/claude-hook-waiter-report.md) |
| `claude-resume-probe/` | Whether hooks or plugins can start or re-arm a monitor without a prompt in Claude Desktop and the CLI | `claude-resume-probe/REPORT.md` |
| `claude-desktop-diagnostic/` | Whether a skill-invoked plugin monitor can wake a Desktop session (no separate write-up) | — |
| `claude-channel/`, `pigeon_claude_wake.py` | Claude Code development-channel wake (alpha.13) | [feasibility](../docs/history/feasibility.md) |
| `claude_plugin_monitor_probe.py`, `session_hook_probe.py` | Claude plugin monitor and startup/prompt hooks in nonpersistent sessions | [session recovery](../docs/session-recovery.md) |
| `codex_*.py`, `pigeon_codex_wake.py` | Codex app-server queue, restart and MCP session environment | [feasibility](../docs/history/feasibility.md), [session recovery](../docs/session-recovery.md) |
| `polling_e2e.py`, `http_mcp_probe.py`, `loopback_probe.py` | Early polling and loopback/sandbox connectivity (pre-topic, pre-HTTP protocol) | [feasibility](../docs/history/feasibility.md) |
