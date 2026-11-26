# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

.NET 11 GA SDK (`global.json` pins 11.0.100), C# 14. `dotnet test` runs on Microsoft Testing Platform with xUnit v3, so test filters are passed straight to `dotnet test`, with no `--`.

```shell
dotnet build Pigeon.slnx
dotnet test --solution Pigeon.slnx
dotnet test --project tests/Pigeon.UnitTests/Pigeon.UnitTests.csproj --filter-method "*Answers_are_read_from_each_kind_of_window"
dotnet test --project tests/Pigeon.UnitTests/Pigeon.UnitTests.csproj --filter-class "Pigeon.UnitTests.VersionTests"
node --test integrations/opencode-plugin/waker.test.ts
node --test integrations/copilot-plugin/waker.test.mjs
dotnet pack src/Pigeon.Cli/Pigeon.Cli.csproj --output artifacts
```

- **Hook tests need a shell:** the Claude hook tests need `bash` and `sh`. On Windows that means Git Bash; set `PIGEON_TEST_BASH` if `bash.exe` is not in its usual place.
- **CI** (`.github/workflows/ci.yml`) builds and tests on Windows, Ubuntu and macOS, and runs the two Node tests above.
- **Integration tests** (`tests/Pigeon.IntegrationTests`, Reqnroll/Gherkin `.feature` files plus `Steps/`) start the real `pigeon` executable from the test output folder, each on a free port from `TestPorts`, so they never touch a running service.

## Architecture

Pigeon lets two AI agent sessions, in the same or different host tools, exchange messages through a private **topic** (at most two members) and wakes the receiving conversation. The design is in `docs/architecture.md`, `docs/session-recovery.md`, `docs/security-design.md` and `docs/remote-design.md`. `docs/history/` holds dated material that may describe older behavior.

One executable, `pigeon` (`src/Pigeon.Cli`, a trimmed, compressed single-file build), plays three roles. `Program.cs` picks the role from its first arguments:

1. **Service** (`pigeon start`, which re-launches itself with `--background`). A local HTTP API on `127.0.0.1:29372` (`PIGEON_PORT`), implemented in `Pigeon.Service` (`LocalHttp.cs`, `Broker.cs`, `BrokerJoins.cs`), backed by the SQLite store in `Pigeon.Core` (`Mailbox.cs`, `WakeState.cs`). The optional machine-to-machine listener (`pigeon peer listen`, port 29374, mutual TLS with certificate pinning) is in `Pigeon.Service/Peers`.
2. **MCP server** (`pigeon mcp`, with `PIGEON_RUNTIME` naming the host). Each host plugin starts one per session over stdio. `McpStdio.cs` and `McpSession.cs` expose the `pigeon_*` tools and forward them to the service's HTTP API. They also show join prompts through MCP elicitation or, for hosts that can't show them, through `NativeDialog.cs`: a fixed PowerShell, osascript or zenity/kdialog window that reads its text only from environment variables.
3. **Command line and plugin helpers**: the human commands (join, send, receive, topics, peer…, `CliHelp.cs` is the source of truth for usage), `pigeon hook …` used by the Claude plugin's hooks (`ClaudeHookWait.cs`, `SessionHook.cs`), and `pigeon update` (`Updater.cs`).

**Wake flow:** the service keeps one durable wake generation per session with waiting messages. A host-specific notifier starts a turn whose fixed instruction names only the generation. The agent then calls `pigeon_receive` with that generation, which leases up to 20 messages, and `pigeon_wake_complete`, which acknowledges them. A lease that isn't completed returns its messages, so delivery is at least once. The notifiers, per host:

| Host | Notifier |
|---|---|
| Claude | `integrations/claude-plugin/scripts/hook-wait.sh` (async hooks) |
| Codex | `CodexQueueNotifier` (`codex queue`) |
| Antigravity | `AntigravityWakeSidecar` |
| OpenCode | `integrations/opencode-plugin/pigeon/waker.ts` |
| Copilot | `integrations/copilot-plugin/extensions/pigeon/waker.mjs` |

`Pigeon.Adapters` holds the C# host-specific parts.

**Plugins:** `integrations/<host>-plugin/` each hold an MCP config that runs `pigeon mcp`, a `connect` skill, and host hooks or wakers. The repository root is also a plugin marketplace: `.claude-plugin/marketplace.json` and `.agents/plugins/marketplace.json` (Codex).

**Trust model:** the local API has no caller authentication, and session IDs are routing labels, not identities. Peer messages and text from other machines are untrusted data. They must never reach a shell or script, which is why `NativeDialog` passes them through environment variables and `NativeDialogTests` checks this.

## Releases and versions

- **Versions move in lockstep.** The CLI `<Version>` in `src/Pigeon.Cli/Pigeon.Cli.csproj` must match:
  - `integrations/claude-plugin/.claude-plugin/plugin.json`
  - `integrations/codex-plugin/.codex-plugin/plugin.json`
  - `integrations/antigravity-plugin/plugin.json`
  - `integrations/copilot-plugin/plugin.json` and `export const version` in its `waker.mjs`
  - `export const version` in `integrations/opencode-plugin/pigeon/waker.ts`

  `VersionTests` fails if any of them differ. Bump them all together, even for an unchanged plugin.
- **Changelog:** every user-visible change gets an entry in `CHANGELOG.md`.
- **Releasing:** `.github/workflows/release.yml` (Installers) builds the MSI (WiX, `packaging/windows`), macOS `.pkg`, and Linux `.deb`/`.rpm`/`.tar.gz` (`packaging/`), and installs and smoke-tests them, including `pigeon update` from an old build. A `v*` tag creates a draft prerelease with the installers and `SHA256SUMS.txt`, and a person publishes it.

## Website

`site/` is the static product site, published by `.github/workflows/pages.yml` to https://andrevianna.github.io/Pigeon/ whenever `site/**` changes on `main`. Its links point to specific release files and tags, so update them on each release. Design sources (briefs, logo, the demo-video renderer) are in `design/site/`.

## Working in this repository

- **Pull requests:** don't open a PR for unfinished work.
- **Live tests:** use disposable databases (`PIGEON_DATABASE`), free ports (`PIGEON_PORT`) and synthetic sessions. Never target the user's running service or real sessions.
