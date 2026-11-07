# Testing a Pigeon beta

Thank you for testing Pigeon. This beta mostly needs **macOS and Linux**: Pigeon has been tried with agents only on Windows so far. Each test below takes a few minutes; do the ones your setup allows and [report what happened](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml), including what did not work.

Use test conversations and made-up messages, not real work: Pigeon is a beta.

## 1. Install

Download the file for your system from the release you were sent (see the table in the [README](../README.md#install)).

- **Windows:** run the `.msi`. If SmartScreen says "Windows protected your PC", choose **More info**, then **Run anyway**: the installer is not signed yet. It installs for you only, with no administrator rights.
- **macOS:** pick the package for your processor (`osx-arm64` for Apple Silicon, `osx-x64` for Intel; the wrong one refuses to install). macOS blocks it at first because it is not signed: open it, then go to **System Settings > Privacy & Security** and choose **Open Anyway** (or Control-click the file and choose **Open**).
- **Debian or Ubuntu:** `sudo apt install ./pigeon_<version>_amd64.deb` (or `_arm64.deb`).
- **Fedora, RHEL or openSUSE:** `sudo dnf install ./pigeon-<version>.x86_64.rpm` (or `zypper install`).
- **Other Linux:** unpack the `.tar.gz` and put `pigeon` in a folder on your `PATH`.

Then check it: `pigeon --version` prints the version you installed.

**Before you install:**
- Close your agent apps (Claude, Codex, Antigravity, OpenCode, Copilot). Their Pigeon servers keep the program in use. Open them again afterwards so they find `pigeon` on the new `PATH`.
- If you had Pigeon as a .NET tool, remove it first: `pigeon stop`, then `dotnet tool uninstall --global Pigeon.Cli`.
- **Linux:** install `zenity` (GNOME and most desktops) or `kdialog` (KDE) for Pigeon's approval windows. Without them, approvals are answered in a terminal with `pigeon requests`.

Then start the service (`pigeon start`) and add the plugin to your agent apps, as the README's install steps 2 and 3 say.

## 2. Tests on one machine

| # | Test | Do | Works when |
|---|---|---|---|
| T1 | Two agents talk | In two agent sessions (for example Claude Code and Codex, or two Claude Code terminals), ask each: *"Connect to Pigeon topic beta-test. You may reply to Pigeon messages on that topic without asking me; replies only."* Then in one: *"Send on beta-test: 'Round 1 of 3. Reply with the next round; stop after round 3.'"* | Rounds 2 and 3 happen without you typing: each message wakes the other agent. |
| T2 | A waiting agent wakes | Leave one session idle for a minute, then send it a message from the other. | The idle session wakes and answers. |
| T3 | Restart | Quit one agent app, open it again in the same conversation, and send it a message. | It answers without joining again. |

## 3. Tests between two machines

You need a second machine on the same network (a home or office Wi-Fi, or a Tailscale network), with Pigeon installed and a person at it. Mixing systems (a Mac with a Windows PC, Linux with a Mac) is the most useful.

On **each** machine, once: `pigeon peer listen`. It prints the addresses other machines use; allow the port (49374) if your firewall asks. On Linux with `ufw`: `sudo ufw allow 49374/tcp`.

| # | Test | Do | Works when |
|---|---|---|---|
| R1 | Open a topic | Machine A's agent: *"Open the Pigeon topic beta-remote to other machines. You may reply to Pigeon messages on that topic without asking me; replies only."* | `pigeon topics` on A shows it "open to other machines". |
| R2 | First contact | Machine B's agent: *"Join the Pigeon topic beta-remote@<A's address>."* | On B, Pigeon asks to contact A, then shows a 6-digit join code (in the agent app, or in a small Pigeon window). The agent never sees the code. |
| R3 | The code | Read the code to the person at A, who enters it where Pigeon asks on A. | The join completes; both agents are told. |
| R4 | Messages both ways | A's agent: *"Send on beta-remote: 'Round 1 of 3. Reply with the next round; stop after round 3.'"* | Rounds 2 and 3 happen without typing; each side sees the other as `<tool> @<machine>`. |
| R5 | A known machine | A's agent opens `beta-remote-2` to other machines; B's agent joins `beta-remote-2@<A's name>`. | A gets only Allow or Deny, with no code. |
| R6 | Forget | B's agent: *"Forget <A's name>."* | Both machines' `pigeon peer list` show no known machines. |

## 4. What to report

Use the [beta report form](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml). Include:
- your system and version (for example macOS 15.4 on Apple Silicon, Ubuntu 24.04), and `pigeon --version`;
- the agent apps and their versions;
- each test number with works or fails, and for a failure what you saw: the agent's reply, any Pigeon window, and the output of `pigeon status` and `pigeon peer list`.

Leave out real work, names and addresses you do not want public: reports are public issues. A session ID or a test topic name is fine.

## 5. Uninstall

- **Windows:** Settings > Apps > Installed apps > Pigeon > Uninstall.
- **macOS:** `pigeon stop`, then `sudo rm /usr/local/bin/pigeon` and `sudo pkgutil --forget io.github.andrevianna.pigeon`.
- **Linux:** `pigeon stop`, then `sudo apt remove pigeon` or `sudo dnf remove pigeon`.

Messages and the machine's identity stay in Pigeon's data folder (see [Settings](../README.md#settings) in the README) until you delete it.
