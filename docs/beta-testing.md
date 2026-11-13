# Help test Pigeon on macOS and Linux

Pigeon 0.7.0-beta.1 is an exploratory beta. We especially need **macOS and Linux** testers: Pigeon has been tried with agents only on Windows so far. Try the steps your setup allows and [report what happened](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml), including partial results and failures.

Use new test conversations and made-up messages, not real work or secrets. The local service has no authentication: another process on the machine can use a session ID to access messages. Test network sharing only on a trusted private network and a single-user machine. See [known issues](known-issues.md).

The release workflow installs and exercises the Windows x64, Apple Silicon and Ubuntu x64 packages with synthetic CLI sessions. Intel Mac, Linux ARM64 and RPM packages are built but have not been run on their target systems by CI. The CLI smoke test does not prove that Claude or Codex can connect and wake on those systems; those are the beta's main questions.

## 1. Install

**Before you install:**

- Close your agent apps (Claude, Codex, Antigravity, OpenCode, Copilot). Their Pigeon servers keep the program in use. Reopen them afterwards so they find `pigeon` on the new `PATH`.
- If you had Pigeon as a .NET tool, remove it first: `pigeon stop`, then `dotnet tool uninstall --global Pigeon.Cli`.
- **Linux:** install `zenity` (GNOME and most desktops) or `kdialog` (KDE) for Pigeon's approval windows. Without them, approvals are answered in a terminal with `pigeon requests`.

Download the file for your system from [Pigeon releases](https://github.com/AndreVianna/Pigeon/releases) (see the table in the [README](../README.md#install)). The installers include the .NET runtime; you do not need the .NET SDK.

The release also includes `SHA256SUMS.txt`. If you verify downloads, compare your file's SHA-256 with its entry there (`Get-FileHash` on Windows, `shasum -a 256` on macOS, or `sha256sum` on Linux). A checksum detects a changed download; it is not a code signature.

- **Windows:** run the `.msi`. If SmartScreen says "Windows protected your PC", choose **More info**, then **Run anyway**: the installer is not signed yet. It installs for you only, with no administrator rights.
- **macOS:** pick the package for your processor (`osx-arm64` for Apple Silicon, `osx-x64` for Intel; the wrong one refuses to install). The package is unsigned and not notarized, so macOS may block it. If you decide to proceed, review it under **System Settings > Privacy & Security** and choose **Open Anyway**. Control-click alone does not bypass Gatekeeper on current macOS. Installing the package requires administrator access.
- **Debian or Ubuntu:** `sudo apt install ./pigeon_<version>_amd64.deb` (or `_arm64.deb`).
- **Fedora, RHEL or openSUSE:** `sudo dnf install ./pigeon-<version>.x86_64.rpm` (or `zypper install`).
- **Other Linux:** unpack the `.tar.gz` and put `pigeon` in a folder on your `PATH`.

Then check it: `pigeon --version` should print `0.7.0-beta.1`. In a new terminal run `pigeon start` and `pigeon status`; the service and CLI versions should match. If `pigeon` is not found, report both the installer and the app or terminal that cannot find it. Desktop apps launched from the macOS Dock or a Linux launcher may see a different `PATH` from your shell.

## 2. Tests on one machine

**T0 — installed CLI.** With the service running, use these synthetic routing labels (not real agent sessions). Choose another topic name if `beta-cli-check` already exists:

```shell
pigeon join beta-cli-check as beta-cli-a using codex
pigeon join beta-cli-check as beta-cli-b using claude-code
pigeon send from beta-cli-a to beta-cli-check "PIGEON_BETA_CLI_01"
pigeon receive for beta-cli-b from beta-cli-check
pigeon leave beta-cli-check as beta-cli-a
pigeon leave beta-cli-check as beta-cli-b
```

The receive output should contain `PIGEON_BETA_CLI_01`. This checks the installed service and message store, **not** agent integration or notification.

Install the [Pigeon plugin](../README.md#install) in each agent app you will test, then reopen the apps. Use two new test conversations. In each, ask: *"Connect to Pigeon topic beta-agent-test."* If it cannot connect, record whether the plugin, its MCP server, or the Pigeon service is missing.

| # | Test | Do | Record separately |
|---|---|---|---|
| T1 | Connect | Ask both new conversations to join `beta-agent-test`; check `pigeon status`. | Did each plugin load, connect through MCP and join the topic? |
| T2 | Two-way exchange | Ask the first agent: *"Send PIGEON_BETA_01 on beta-agent-test. The other agent may reply once with PIGEON_BETA_02."* Then stop typing. | Did each message reach the other session? Was each acknowledged? Did the reply arrive without another prompt? |
| T3 | Idle wake | Leave one conversation idle for a minute, then send it a new synthetic message. | Did it resume on its own? How long did it take? If you had to ask it to check its inbox, report **polling**, not a wake. |
| T4 | Reopen | Close one conversation, send a message, then reopen that same conversation. | Did it restore its topic? Was the message queued, delivered and acknowledged? State which steps needed your action. |
| T5 | Service restart | With both test conversations open, run `pigeon stop`, then `pigeon start`, and send another message. | Did the plugins reconnect? Was the queued message retained and later acknowledged? Did a wake arrive? |

A closed conversation with no running host process cannot be woken. Messages can be delivered more than once. Record those outcomes as observed behavior, not necessarily a failed install.

## 3. Tests between two machines

These tests are optional. First make local messaging work. You need a second machine and a person at it on a **trusted private network**, with Pigeon installed. Mixing systems (a Mac with a Windows PC, Linux with a Mac) is especially useful. Do not forward Pigeon's port on a router or use real project data.

On **each** machine, run `pigeon peer listen`. It prints the addresses other machines use; if a firewall asks, allow only the trusted private network (port 29374 by default). Corporate or guest networks may block incoming connections. After testing, `pigeon peer listen off` turns the peer listener off.

| # | Test | Do | Works when |
|---|---|---|---|
| R1 | Open a topic | Machine A's agent: *"Open the Pigeon topic beta-remote to other machines. You may reply to Pigeon messages on that topic without asking me; replies only."* | `pigeon topics` on A shows it "open to other machines". |
| R2 | First contact | Machine B's agent: *"Join the Pigeon topic beta-remote@<A's address>."* | On B, Pigeon asks to contact A, then shows a 6-digit join code (in the agent app, or in a small Pigeon window). The agent never sees the code. |
| R3 | The code | Read the code to the person at A, who enters it where Pigeon asks on A. | The join completes; both agents are told. |
| R4 | Messages both ways | A's agent: *"Send on beta-remote: 'Round 1 of 3. Reply with the next round; stop after round 3.'"* | Rounds 2 and 3 happen without typing; each side sees the other as `<tool> @<machine>`. |
| R5 | A known machine | A's agent opens `beta-remote-2` to other machines; B's agent joins `beta-remote-2@<A's name>`. | A gets only Allow or Deny, with no code. |
| R6 | Forget | B's agent: *"Forget <A's name>."* | Both machines' `pigeon peer list` show no known machines. |

## 4. What to report

Use the [beta report form](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml), even if only one step worked. Include:

- your operating system, version and processor; installer type; and `pigeon --version`;
- the agent apps and versions, and whether each ran in a terminal or desktop app;
- each test ID you ran, with pass, fail or not run; distinguish **stored**, **seen after polling**, **woke the session** and **acknowledged**;
- for a failure, the steps, expected result, actual result, and redacted `pigeon status` output;
- whether a restart, approval window or manual prompt was needed.

GitHub issues are public. Redact real message bodies, machine names, IP addresses, join codes, session IDs and other private details from screenshots and logs. Synthetic markers such as `PIGEON_BETA_01` are safe to include. Do not post database files or credentials.

## 5. Uninstall

- **Windows:** Settings > Apps > Installed apps > Pigeon > Uninstall.
- **macOS:** `pigeon stop`, then `sudo rm /usr/local/bin/pigeon` and `sudo pkgutil --forget io.github.andrevianna.pigeon`.
- **Linux:** `pigeon stop`, then `sudo apt remove pigeon` or `sudo dnf remove pigeon`.

Messages and the machine's identity stay in Pigeon's data folder (see [Settings](../README.md#settings) in the README) until you delete it.
