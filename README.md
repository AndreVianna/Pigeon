# Pigeon

Pigeon lets two AI agent sessions on the same Windows computer talk to each other, for example a Claude conversation and a Codex chat working on the same project. One agent sends a message; the other is woken up, reads it, and can reply. You do not have to copy text between windows.

Conversations happen in **topics**. A topic is a private conversation between exactly two sessions, such as `parser-review`. Messages wait in order until the other side reads them, even if it is busy, idle, or has not joined yet.

Everything stays on your computer. Pigeon runs a small background service that only accepts connections from this machine.

## What you need

- Windows 10 or 11.
- The [.NET 11 runtime](https://dotnet.microsoft.com/download) (ASP.NET Core Runtime).
- [Git for Windows](https://git-scm.com/download/win), if you use Claude Code: the Claude plugin's hook runs under Git Bash.
- Claude Code (including Claude Desktop) and/or Codex (including the ChatGPT desktop app).

## Install

**1. Install the `pigeon` command.** Pigeon is not on nuget.org yet. Build the package from this repository and install it as a .NET tool:

```powershell
dotnet pack src/Pigeon.Cli/Pigeon.Cli.csproj --output artifacts
dotnet tool install --global Pigeon.Cli --version 0.5.0-beta.2 --source .\artifacts
```

To update an earlier version, use `dotnet tool update` with the same arguments, then run `pigeon stop` and `pigeon start` so the service runs the new version.

**2. Start the service.**

```powershell
pigeon start
```

It runs in the background until you stop it or restart the computer. `pigeon status` tells you whether it is running.

**3. Add the plugin to your agent tools.** This repository is a plugin marketplace named `pigeon` for both tools.

- **Claude Code / Claude Desktop:** in Claude Code run `/plugin install pigeon --marketplace AndreVianna/Pigeon`, or in Claude Desktop add `AndreVianna/Pigeon` as a marketplace in the plugin settings and install **pigeon**.
- **Codex / ChatGPT desktop app:** run `codex plugin marketplace add AndreVianna/Pigeon`, or add the repository in the app's Marketplace tab, then install **pigeon** and approve its hooks when asked. If you added Pigeon earlier as a plain MCP server, remove that server first.

Install the plugin, not just an MCP server: the plugin also brings the hooks that wake the agent and the `connect` skill.

## Connect two agents

1. In the first agent, ask it to connect to a topic. In Claude, type `/pigeon:connect parser-review`. In Codex, ask: *"Connect to Pigeon topic parser-review."* The agent joins and reports that it is waiting for a partner.
2. Do the same in the second agent with the same topic name. It joins as the partner.
3. Ask either agent to send something to the other, for example *"Ask Codex to review the parser changes."*

From then on, each message wakes the other agent, which reads it and can answer on the same topic, without anyone typing. Agents treat messages from each other as information, not as orders: a message never gives the other agent permission to do something you have not allowed.

A topic holds two sessions. To talk to a third agent, use another topic, such as `parser-review-cursor`.

## Commands

Words such as `as`, `using`, `from`, `to` and `for` are part of the commands. A *session ID* is the ID your agent tool gives its conversation; the plugins find it for you, so you need it only when you use the commands yourself. `pigeon status` and `pigeon topics` show the session IDs in use.

| Command | What it does |
|---|---|
| `pigeon start` | Starts the background service. `--foreground` keeps it in the current window instead. |
| `pigeon stop` | Stops the service. Waiting messages are kept. |
| `pigeon status` | Shows whether the service runs, which sessions are connected, and what is waiting. |
| `pigeon topics` | Lists topics, their two members and how many messages are waiting. |
| `pigeon join <topic> as <session-id> using <tool>` | Joins a topic, creating it if it is new. `<tool>` is the program the session belongs to, such as `claude-code` or `codex`. |
| `pigeon leave <topic> as <session-id>` | Leaves a topic. The last one to leave deletes it. |
| `pigeon send from <session-id> to <topic> "<message>"` | Sends a message to the other member. Use `-` as the message to read it from standard input. |
| `pigeon receive for <session-id> from <topic>` | Shows the messages waiting for that session and removes them from the queue. |
| `pigeon --help`, `pigeon <command> --help` | Shows help. `pigeon --version` shows the version. |

`pigeon mcp` and `pigeon hook` are used by the plugins; you do not run them yourself.

Example:

```powershell
pigeon join parser-review as 01a19407-57ce using codex
#   Joined parser-review. Waiting for a partner.
pigeon send from 01a19407-57ce to parser-review "Ready for review"
#   The recipient is unavailable: nobody else is in parser-review. Your message is held until someone joins (up to 24 hours).
pigeon join parser-review as b1f82365-c3a4 using claude-code
#   Joined parser-review with codex 01a19407-57ce.
pigeon receive for b1f82365-c3a4 from parser-review
#   [2026-10-31 18:25:14Z] codex 01a19407-57ce: Ready for review
```

Commands exit with code 0 when they succeed (including when a message is held for later), 1 when Pigeon refuses or cannot do something (for example a full topic or a stopped service), and 2 when the command itself is written wrongly.

## How topics behave

- **Two members.** The first session to join creates the topic and waits; the second completes it. A third is refused, and the message names the two members so you can decide who should leave.
- **Messages wait.** A message sent while the other member is away, or before anyone else has joined, is kept and delivered when that session returns or joins. Messages are kept for up to 24 hours.
- **Replacing a member.** If a member leaves, its unread messages go to whoever joins in its place.
- **Cleaning up.** The last member to leave deletes the topic. A topic whose two members have not been connected for an hour is deleted automatically, with any messages still waiting in it.
- **Limits.** A topic accepts up to 60 messages an hour, which stops two agents from answering each other in an endless loop.

## Things to know

- **Agents are woken only while their conversation is open.** A closed conversation cannot be woken. When you reopen it, its messages are waiting, and the plugin restores its topics.
- **Claude renewals.** To stay reachable, the Claude plugin's background waiter is renewed every 12 hours with a short turn that you may see in an idle conversation. It is not a message, and Claude ends it without doing anything else.
- **No passwords.** Pigeon does not verify who is who. Any program running on this computer, under any account, can read Pigeon's messages, use a session ID, or stop the service. Do not send secrets through it.
- **At least once.** If an agent stops halfway through reading, its messages are delivered again, so the same message can occasionally arrive twice.

More detail is in [known issues](docs/known-issues.md) and [local trust boundaries](docs/security-design.md).

## Settings

Set these environment variables before `pigeon start` (and, for the port, for the agent tools too) to change the defaults.

| Variable | Default | Meaning |
|---|---|---|
| `PIGEON_PORT` | `49372` | The local port the service uses. The service, the plugins and the commands must all use the same one. |
| `PIGEON_DATABASE` | `%LOCALAPPDATA%\Pigeon\messages.db` | Where messages are stored (an absolute path). |
| `PIGEON_MAX_MESSAGES_PER_HOUR` | `60` | Messages one topic accepts per hour. |
| `PIGEON_ABANDONED_TOPIC_MINUTES` | `60` | How long both members can be away before a topic is deleted. |
| `PIGEON_BODY_RETENTION_DAYS` | not set | When set, the text of read or expired messages is erased after this many days. This is not secure erasure: database files and backups can still contain it. |
| `PIGEON_MAX_ATTEMPTS`, `PIGEON_MAX_WAKE_ATTEMPTS`, `PIGEON_WAKE_RETRY_SECONDS` | `3`, `3`, `120` | How often an unread message and a wake are retried. |

## Upgrading from an earlier version

Version 0.5 replaced named agents and multi-member topics with two-member topics. When the new service first opens an older database, it keeps each session's topics with up to two members (the two most recently active) and their waiting messages. Older name-based records are no longer used but are left in the database file. Restart the agent tools after updating so their plugins use the new commands. The changes are listed in [CHANGELOG.md](CHANGELOG.md).

## For developers

The solution `Pigeon.slnx` targets .NET 11 and C# 14 and contains xUnit unit tests and Reqnroll (Gherkin) integration tests. The Claude hook tests need Git Bash; if it is not in its usual place, set `PIGEON_TEST_BASH` to its executable.

```powershell
dotnet build Pigeon.slnx
dotnet test Pigeon.slnx
dotnet pack src/Pigeon.Cli/Pigeon.Cli.csproj --output artifacts
```

Versions move in lockstep: the CLI (`<Version>` in `src/Pigeon.Cli/Pigeon.Cli.csproj`), the Claude plugin and the Codex plugin always share one version, and a release bumps all three together even when a plugin is unchanged. A unit test fails if they differ.

The design is described in [architecture](docs/architecture.md), [session recovery](docs/session-recovery.md) and [local trust boundaries](docs/security-design.md); planned work is in the [release plan](docs/release-plan.md). Dated research, plans and test reports are kept in [docs/history](docs/history) and may describe earlier behavior. Host-test fixtures are indexed in [experiments](experiments/README.md).

## License

Pigeon is open source under the [MIT License](LICENSE).
