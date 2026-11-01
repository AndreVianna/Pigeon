# Changelog

The CLI and the Claude and Codex plugins share one version (see the README's build section).

## 0.5.0-beta.1 (unreleased)

First beta. The CLI moves from 0.1.0-alpha.19, the Claude plugin from 0.4.0 and the Codex plugin from 0.3.0 to one shared version.

### Fixed
- A busy or locked database is reported as "storage unavailable" instead of a generic error; the SQLite busy timeout (2 s) is shorter than the client timeout, and a rollback runs only inside a transaction.
- A new message re-arms an exhausted wake.
- Tool names are compared consistently, so sessions registered with the legacy runtime name "Claude Code" can claim wakes.
- The Claude hook waiter tracks its whole launcher chain, so killing its outer Git Bash shell stops it before it can claim a wake into a closed pipe.
- A hook waiter that owned its session still requests its renewal when the service is unavailable at the deadline.
- The Codex plugin now loads its MCP server and hooks: it uses the `.codex-plugin/plugin.json` layout with a `.mcp.json`, which Codex requires for local servers.

### Added
- `pigeon status` shows the service version, warns when it differs from the CLI, and flags Claude sessions with no notifier since the service started.
- The repository is a plugin marketplace named `pigeon` for both Claude Code (`.claude-plugin/marketplace.json`) and Codex (`.agents/plugins/marketplace.json`).

### Changed
- The retention sweep runs at most once a minute per project.
- The MCP server reports the CLI's version.

### Known limits
- No application-level authentication; a tool/session pair is a routing claim.
- Delivery is at least once; duplicates are possible.
- A conversation whose process the host has not started (after a reopen or app restart) cannot be woken.
- On Claude Desktop the hook waiter's chain ends at its top Git Bash shell, so it does not notice a conversation process that exits while those shells survive; it stops at its deadline.
- The 12-hour hook renewal has not yet run on the host.

See [known issues](docs/known-issues.md) for the full list.
