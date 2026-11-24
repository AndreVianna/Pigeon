# Pigeon website: product brief and page copy

Prepared 2026-11-24 for Pigeon 0.7.0-beta.2. This file gives the UX designer the content and structure of the public site at `https://andrevianna.github.io/Pigeon/`. It does not specify visuals; the visual direction (Sleek Dark Mode, charcoal, one violet accent, monospace for code) is already chosen and lives in `design/visuals`.

Every product claim below comes from the repository at the `v0.7.0-beta.2` tag: `README.md`, `CHANGELOG.md`, `docs/beta-testing.md`, `docs/known-issues.md`, `docs/security-design.md`, `docs/remote-design.md`, the CLI source, and the published release. Where a snippet could not be captured from a running 0.7.0-beta.2, it is marked "confirm at capture".

## How to read this file

- Sections 1 and 2 are background for the designer. Sections 3 to 6 are what goes on the pages. Section 7 is for the owner.
- Text in a "Copy" block is final and can be placed as written. Text in a "Job" or "Visual" line is direction, not copy.
- Copy strings are wrapped in backticks here only to mark where they start and end. Inside them, set commands (such as pigeon start), file names, paths and topic names in monospace.
- Named links such as `[RELEASE]` are defined once in "Links and facts" so that a new release changes one table.

## Links and facts

The links and version-specific values in one place. A new release changes this table and every place the version is written out in the copy; open question 2 lists those places.

| Name | Value |
|---|---|
| Version | `0.7.0-beta.2` (prerelease, published 2026-11-23) |
| `[SITE]` | `https://andrevianna.github.io/Pigeon/` |
| `[REPO]` | `https://github.com/AndreVianna/Pigeon` |
| `[RELEASE]` | `https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.2` |
| `[ASSETS]` | `https://github.com/AndreVianna/Pigeon/releases/download/v0.7.0-beta.2/` followed by a file name |
| `[BETA_GUIDE]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/beta-testing.md` |
| `[KNOWN_ISSUES]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/known-issues.md` |
| `[REMOTE_DESIGN]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/remote-design.md` |
| `[LOCAL_TRUST]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/security-design.md` |
| `[README]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/README.md` |
| `[CHANGELOG]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/CHANGELOG.md` |
| `[REPORT]` | `https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml` |
| `[FEATURE]` | `https://github.com/AndreVianna/Pigeon/issues/new/choose` |
| `[LICENSE]` | `https://github.com/AndreVianna/Pigeon/blob/main/LICENSE` |

Two traps:

- Do not link to `/releases/latest`. GitHub's "Latest" release is 0.5.0, which is older than the beta and ships only a .NET tool package, with no installers. Link to `[RELEASE]` or to a file under `[ASSETS]`.
- The asset names are not uniform. Use the exact names in the Install page table (for example `pigeon_0.7.0.beta.2_amd64.deb` and `pigeon-0.7.0-0.beta.2.x86_64.rpm`), not a pattern.

---

## 1. Audience

### Primary: a developer who already runs two AI coding agents on one project

- **Who:** an individual developer, often working alone, who keeps two agent sessions open on the same repository. The typical pair is Claude Code and Codex: different models, different strengths, or a way to spread token use. One writes and the other reviews, or each owns a part of the code.
- **What they already use:** a terminal, Git and GitHub, and at least two of Claude Code or Claude Desktop, Codex or the ChatGPT desktop app, the Antigravity App, OpenCode 2, GitHub Copilot. They install agent plugins and know roughly what an MCP server is. They read a README before a landing page and distrust marketing language.
- **Their pain, as they say it:** "Claude had a question for Codex, so I copied it over. Codex answered, so I copied that back. I am the messenger."
- **Most wanted inside this group:** people on macOS and Linux. Pigeon has been tried with agents only on Windows, so their reports are the beta's main purpose.

### Secondary

1. **Two machines, one or two people.** A developer with a desktop and a laptop, or two colleagues on the same LAN or private overlay network, who want an agent on each machine to share a topic.
2. **Evaluators.** Editors of developer-tool listings, reviewers of plugin directories, and maintainers of curated lists. They need a credible summary, a clear statement of maturity and a link they can run within minutes.
3. **Returning testers.** People who installed a beta and come back to update, find the report form, or check a limit.

### What the reader must believe to try it

1. **It is about my existing sessions.** Pigeon connects conversations I already have open. It is not another agent framework and does not start agents.
2. **It works with the tools I have.** My pair is on the supported list, and the page says how each one behaves.
3. **It takes minutes and leaves no mess.** One installer, one command to start, one plugin per tool. No account, no cloud service, nothing that survives an uninstall except a data folder I can delete.
4. **The other agent does not get my permissions.** A message is information for my agent, not an instruction, and it never grants permission. My tool's own approval rules still decide what my agent may do.
5. **The author tells me what does not work.** The page states what has been tried, what has not, and what the beta does not protect against. For this audience that list raises trust.

---

## 2. Positioning

### One-sentence value proposition

Pigeon lets two AI coding sessions you already have open, in the same tool or in different ones, send each other messages that wait until they are read and wake the other session while its conversation is open and its tool allows it, so you stop carrying text between windows.

### The problem in the reader's words

"I have two agents on the same repo. When one needs something from the other, I copy the question across, wait, and copy the answer back. Each trip needs me at the keyboard, and both agents sit idle whenever I am not."

### What people do today, and how Pigeon differs

| Today | What is good about it | Where it falls short | Pigeon, honestly |
|---|---|---|---|
| **Copy and paste between windows.** You are the messenger. | No setup. Works with every tool. You read every message before it goes across. | Needs you present for each trip. Both agents stop when you step away. Long answers get trimmed or mangled in transit. | The sessions send to each other, and the receiving one is woken. You give up the per-message review: you can still read both conversations, and a topic accepts at most 60 messages an hour, but nothing waits for your approval. |
| **A shared file both agents read**, such as a handoff note in the repository. | Durable, visible in the editor, versioned with the code. Good for long-lived shared state. | Nothing tells the other agent that the file changed, so you still prompt it to look. Two agents writing one file collide. The notes end up in commits. | Messages arrive in order, each side has its own queue, and the other session is woken. Pigeon is not a replacement for a document: messages are held for up to 24 hours, not kept as a record, and Pigeon does not stop two agents editing the same source file. |
| **One tool's own subagents or agent teams.** | One place to steer from. The tool manages the handoffs. | It stays inside one tool, and the helpers are new agents without the context your other open conversation already has. | Pigeon connects two conversations that already exist, across tools. It does not orchestrate: there is no task assignment, no shared memory, and a topic has exactly two members. |

Positioning line for listings and social cards: "Messaging between two AI coding sessions you already have open."

### Words to keep and words to avoid

- Use: session, topic, message, wake, join, waits, held, beta, tried, not yet tried.
- "Durable" always appears with its limits: messages are held for up to 24 hours and can occasionally arrive twice.
- "Wake" always appears with its condition: the conversation must be open and the tool must support it.
- "Network" always appears with "trusted private" in front of it.
- Do not write that Pigeon is secure, private or encrypted without the specific statement that is true. Do not promise exactly-once delivery, wakes after a reopen, or behavior on macOS, Linux or Tailscale. Do not write that Pigeon stops an agent from acting on a message; it grants no permission, and the agent's own tool decides the rest.

---

## 3. Page map

Three pages and a 404. The beta guide, known issues and design documents stay on GitHub as the single source; the site links to them and does not copy them, so they cannot drift apart.

| Page | Path | Why it exists |
|---|---|---|
| **Home** | `/Pigeon/` | The landing page. Explains the problem and the product, shows it running, states where the beta stands, and sends the reader to install. |
| **Install** | `/Pigeon/install/` | The destination of the primary button. A GitHub release lists ten files with no guidance; this page picks the right file per system, walks through the unsigned-installer prompts, and covers the plugin step for five tools. Too long for the landing page. |
| **Security and limits** | `/Pigeon/security/` | The audience gives agents shell access and will ask what Pigeon exposes. A full answer on the landing page would bury the product; a full page lets Home state each limit in one sentence and link here. |
| **Not found** | `/Pigeon/404.html` | GitHub Pages serves it for wrong paths. |

Not given their own pages: the beta guide (GitHub, versioned with the release), a changelog (GitHub), a blog, a docs section, a pricing page (Pigeon is free).

Site-wide notes for the designer:

- The site is a project site under `/Pigeon/`, so internal links must be relative or start with `/Pigeon/`.
- Terminal transcripts are real text in `<pre><code>`, not images, with a "Copy" button on every command block. Output lines are visually quieter than command lines.
- Tabs (operating system, agent tool) must show all panels stacked when JavaScript is off. A small inline script may preselect the visitor's operating system and handle copy buttons; it must make no network request.
- No analytics, no cookies, no third-party embeds, no forms. See "Success measures". The kit's README loads Inter and JetBrains Mono from Google Fonts, which is a third-party request; host the fonts with the site instead so the "nothing is tracked" statement in the footer is true.
- Use tool names as text. Do not use the logos of Claude, Codex, Antigravity, OpenCode or Copilot.

### Home: sections in order

| # | Section | Job |
|---|---|---|
| H0 | Announcement bar | Say in one line that this is a public beta and that Mac and Linux testers are wanted. |
| H1 | Header | Navigation and the primary button, always in reach. |
| H2 | Hero | State the outcome in one sentence, show it running, offer the primary and secondary actions. |
| H3 | The problem | Make the reader recognize their own copy-and-paste routine. |
| H4 | How it works | Show that it is three actions inside tools they already use. |
| H5 | How a topic behaves | Give the rules and their numbers, so "messages wait" and "wake" are precise. |
| H6 | Works with | Let the reader find their tools and see how each is woken and where it has been tried. |
| H7 | From the command line | Proof without an agent. Present only once the hero holds a recording (see H2). |
| H8 | Other machines | Present the optional network feature with its conditions attached. |
| H9 | Where the beta stands | State what has been tried, what has not, and four things to know before installing. |
| H10 | Install in three steps | Convert. Show how short the path is and send the reader to the Install page. |
| H11 | Help test | Ask for reports and make the form one click away. |
| H12 | Questions | Remove the remaining objections. |
| H13 | Footer | License, author, source, and the documents on GitHub. |

### Install: sections in order

| # | Section | Job |
|---|---|---|
| I1 | Title and "before you start" | Set the version and the three precautions before anything is downloaded. |
| I2 | Step 1: download and install | Right file per system, with the unsigned-installer steps at the moment the warning appears. |
| I3 | Step 2: start the service | One command, and how to tell it worked. |
| I4 | Step 3: add the plugin | Per-tool instructions. |
| I5 | Step 4: connect two agents | First message, so the reader finishes with something working. |
| I6 | Check it without an agent | A six-line test that separates "Pigeon works" from "my agent tool is connected". |
| I7 | Update and uninstall | Keep testers on the newest beta; show the way out. |
| I8 | If something goes wrong | The four failures the documentation already knows about. |
| I9 | Report your results | Close the loop for the beta. |

### Security and limits: sections in order

| # | Section | Job |
|---|---|---|
| S1 | Title and summary | The whole model in five sentences for readers who stop there. |
| S2 | On your computer | The local service has no caller authentication: what that means and what to do. |
| S3 | What one agent can do to another | Messages are data; permissions stay with your tool. |
| S4 | Between machines | What the network feature does, what another machine can and cannot do, accepted limits. |
| S5 | Installers and updates | Unsigned packages, checksums, and what `pigeon update` checks. |
| S6 | What is stored | Where messages live and that they are not encrypted. |
| S7 | Delivery limits | At least once, 24 hours, one hour. |
| S8 | Report a security problem | Where to send it. |

---

## 4. Final copy

### Home

**Page title (`<title>`):** `Pigeon: let two AI coding agents message each other`

**Meta description:** `Pigeon lets two AI coding sessions, such as Claude Code and Codex, send each other messages that wait and can wake the other side. Open source, public beta.`

**Social card:** `og:title` same as the title. `og:description`: `Stop carrying messages between your AI coding agents. Free and open source, for Windows, macOS and Linux. Public beta.` `og:image`: a 1200 by 630 image in the site's style showing the two-window diagram from H3 (right half only) with the line "Stop being the messenger." Alt text for the image: `Two agent windows, Claude Code and Codex, exchanging messages through Pigeon.`

#### H0. Announcement bar

**Copy:**

> Public beta 0.7.0-beta.2. Mac and Linux testers wanted. **How to help**

"How to help" links to `#help-test` (H11). The bar has no close button, so it needs no stored state. It scrolls away with the page.

#### H1. Header

**Copy:**

- Wordmark: `Pigeon`
- Navigation: `How it works` (`#how`), `Works with` (`#works-with`), `Other machines` (`#machines`), `Beta status` (`#beta`), `Security` (`/Pigeon/security/`), `GitHub` (`[REPO]`, opens the repository)
- Button: `Download the beta` (`/Pigeon/install/`)

On narrow screens keep the wordmark and the button; the rest goes in a menu.

#### H2. Hero

**Job:** one sentence of outcome, one of mechanism, proof on screen, two actions.

**Copy:**

- Eyebrow: `Public beta · Windows, macOS, Linux`
- Headline: `Stop being the messenger between your AI agents.`
- Supporting text: `Pigeon lets two agent sessions you already have open, such as Claude Code and Codex, send each other messages. A message waits until the other session reads it, and Pigeon wakes that session if its conversation is open and its tool allows it. You stop copying text between windows.`
- Primary button: `Download the beta` (`/Pigeon/install/`)
- Secondary button: `Read the beta guide` (`[BETA_GUIDE]`)
- Line under the buttons: `Version 0.7.0-beta.2 · Free and open source (MIT) · Runs on your computer, with no account`

**Visual:** a terminal transcript carries this better than any illustration. Two versions:

*Version A, available today.* One terminal window with the README's example, verbatim. Caption it honestly as the command line, since it shows two stand-in sessions and not two agents.

```console
$ pigeon join parser-review as 01a19407-57ce using codex
Joined parser-review. Waiting for a partner.

$ pigeon send from 01a19407-57ce to parser-review "Ready for review"
The recipient is unavailable: nobody else is in parser-review. Your message is held until someone joins (up to 24 hours).

$ pigeon join parser-review as b1f82365-c3a4 using claude-code
Joined parser-review with codex 01a19407-57ce.

$ pigeon receive for b1f82365-c3a4 from parser-review
[2026-10-31 18:25:14Z] codex 01a19407-57ce: Ready for review
```

- Caption: `The same thing your agents do through the plugin, typed by hand: join a topic, send, and the message waits for the other side.`
- Alt text (if rendered as an image anywhere, for example the social card): `Terminal showing a session joining the topic parser-review, sending "Ready for review", and a second session joining and receiving that message.`

*Version B, to be captured by the owner, replaces A when it exists.* Two agent panes side by side, Claude Code on the left and Codex on the right, as a 30 to 60 second silent recording or a stepped text replay. The channel research in `docs/beta-launch-channels.md` names a real recording as the main missing proof. Shot list, using only made-up content:

1. Left pane, typed by the person: `/pigeon:connect parser-review`. The agent reports that it joined and is waiting for a partner.
2. Right pane, typed by the person: `Connect to Pigeon topic parser-review.` The agent reports that it joined with the other session.
3. Left pane, typed by the person: `Ask Codex to review the parser changes.`
4. Right pane, nobody typing: the session wakes, reads the message and replies on the topic.
5. Left pane, nobody typing: the session wakes and shows the reply.

Show the agents' real replies from the capture; do not write them. Hide session IDs. Record it as a local video file or rebuild it as text in the page; do not embed a third-party player.

- Caption for B: `Recorded on Windows with Claude Code and Codex. Nobody typed after the third prompt.`
- Alt text for B: `Two agent windows side by side. A person asks Claude Code to send Codex a review request; Codex wakes, reads it and replies, and Claude Code wakes to show the reply, with no further typing.`

#### H3. The problem

**Job:** recognition. Short.

**Copy:**

- Headline: `Two agents on one project, and you in the middle`
- Body: `Claude Code has a question for Codex, so you copy it across. Codex answers, so you copy that back. Each trip needs you at the keyboard, and both agents wait whenever you step away.`
- Second line: `With Pigeon, the two sessions send to each other directly. You read along if you want to.`

**Visual:** a before-and-after diagram, two panels. Before: two agent windows with a person carrying a message between them. With Pigeon: the same two windows with a two-way arrow through a small Pigeon node. Under the right panel: `On the same machine, or across a trusted private network.` The concept exists as `docs/media/pigeon-linkedin-infographic-v5.png`, drawn on a light background; redraw it in the site's style and do not present it as a screenshot.

- Alt text: `Before: a person carries a message between Claude Code and Codex. With Pigeon: the two tools exchange messages through Pigeon, on the same machine or within a trusted private network.`

#### H4. How it works

**Job:** three actions, each inside a tool the reader already uses.

**Copy:**

- Section id: `how`
- Headline: `Three things to say to your agents`
- Intro: `A topic is a private conversation between exactly two sessions. You name it; the agents do the rest.`

Step 1

- Title: `Connect the first session`
- Text: `In Claude, type the command below. In Codex, Antigravity, OpenCode or Copilot, ask in words. The agent joins the topic and reports that it is waiting for a partner.`
- Snippets (two small blocks, labeled "Claude" and "Other tools"):

```text
/pigeon:connect parser-review
```

```text
Connect to Pigeon topic parser-review.
```

Step 2

- Title: `Connect the second session`
- Text: `Do the same in the other agent, with the same topic name. It joins as the partner.`

Step 3

- Title: `Ask one to talk to the other`
- Text: `From then on, each message wakes the other agent while its conversation is open. It reads the message and can answer on the same topic, without anyone typing.`
- Snippet:

```text
Ask Codex to review the parser changes.
```

- Line under the steps: `Agents treat messages from each other as information, not as orders. A message never gives the other agent permission to do something you have not allowed.`

**Visual:** a small architecture diagram under the steps; words alone do not show that everything stays on one machine.

```text
agent A ── plugin ──┐                       ┌── plugin ── agent B
                    │  Pigeon service        │
                    └─ on your computer ─────┘
                       (message queue)
```

- Diagram labels: `Your first agent`, `Pigeon plugin`, `Pigeon service on your computer`, `Message queue`, `Pigeon plugin`, `Your second agent`.
- Caption: `Each agent talks to Pigeon through a plugin. The service runs on your computer and keeps the messages in a local database.`
- Alt text: `Diagram: two agents, each connected by a Pigeon plugin to one Pigeon service running on the same computer, which holds the message queue.`

#### H5. How a topic behaves

**Job:** the rules, with numbers. A dense grid of six short cards suits the visual style.

**Copy:**

- Headline: `What a topic does, exactly`

| Card title | Card text |
|---|---|
| `Two members` | `The first session to join creates the topic and waits. The second completes it. A third is refused. For a third agent, use another topic.` |
| `Messages wait` | `A message sent while the other session is busy, away, or not yet joined is kept in order and delivered when it returns. Messages are held for up to 24 hours.` |
| `Wake while open` | `An agent is woken only while its conversation is open. A closed conversation cannot be woken; when you reopen it, its messages are waiting.` |
| `No endless loops` | `A topic accepts up to 60 messages an hour, which stops two agents from answering each other without end.` |
| `Information, not orders` | `A message grants no permission in the other agent's tool. What that agent may do is still decided by the rules you set there.` |
| `At least once` | `If an agent stops halfway through reading, its messages are delivered again, so the same message can occasionally arrive twice.` |

- Line under the grid: `One more rule: a topic whose two members have both been disconnected for an hour is deleted, with any messages still waiting in it.` Link: `All known issues` (`[KNOWN_ISSUES]`).

#### H6. Works with

**Job:** the reader finds their tools and sees, per tool, how a waiting message reaches an idle conversation and where that has been tried. A table carries this better than prose or a logo row.

**Copy:**

- Section id: `works-with`
- Headline: `Works with the agent tools you already run`
- Intro: `Each tool gets a Pigeon plugin. The two sessions can be in the same tool or in different ones.`

| Tool | How an idle conversation is woken | Tried so far |
|---|---|---|
| `Claude Code and Claude Desktop` | `The plugin's hook wakes the open conversation.` | `Windows` |
| `Codex and the ChatGPT desktop app` | `The plugin puts a wake prompt in the open chat.` | `Windows` |
| `Antigravity App 2.28 or later` | `Pigeon wake, which you turn on once in Antigravity's Automations dashboard. The Antigravity CLI and IDE can send and receive but are not woken.` | `Windows, with a development copy of the plugin` |
| `OpenCode 2, CLI or Desktop` | `The plugin queues a prompt through OpenCode's background server. OpenCode 1.x is not supported.` | `Windows, OpenCode 2.2.5, with a development copy of the plugin` |
| `GitHub Copilot: the app, the CLI, and Copilot chats in VS Code` | `A Copilot extension queues a prompt. VS Code's older Local chats are not woken.` | `Windows, in VS Code and the Copilot app, with a development copy of the plugin. Copilot CLI not yet tried.` |

- Note under the table: `On macOS and Linux, Pigeon passes its automated tests, and the Apple Silicon and Ubuntu x64 installers are installed and checked on every release. No agent tool has been tried there yet. That is the main question of this beta.` Link: `Help test on Mac or Linux` (`#help-test`).

#### H7. From the command line

**Job:** proof without an agent, and a signal that the tool is inspectable. Include this section only when the hero shows the recording (version B). While the hero shows the command-line transcript (version A), leave H7 out.

**Copy:**

- Headline: `Everything the agents do, you can do by hand`
- Body: `The pigeon command joins, sends and receives like an agent does, and shows what is waiting. Commands read like sentences.`
- Snippet: the transcript from H2 version A, followed by:

```console
$ pigeon topics
parser-review: codex 01a19407-57ce and claude-code b1f82365-c3a4 | 0 waiting
```

The `pigeon topics` line follows the format in the CLI source and is to be confirmed at capture on 0.7.0-beta.2.

- Link: `All commands` (`[README]#commands`)

#### H8. Other machines

**Job:** present the optional network feature with its conditions in the same breath, so nobody reads "network" without "trusted private".

**Copy:**

- Section id: `machines`
- Eyebrow: `Optional, off by default`
- Headline: `Two machines, one topic, on a network you trust`
- Body: `Agents on two machines on the same trusted private network, such as your home or office LAN, can share a topic. That includes a colleague's machine. The two Pigeon services talk directly over TLS, each checks the other's certificate, and a person on each side approves the first connection.`

Steps:

1. `On each machine, once, a person runs pigeon peer listen. It prints the addresses other machines can use.`
2. `Ask your agent to open a topic to other machines.`
3. `On the other machine, ask the agent there to join it by address, for example review@192.168.2.20.`
4. `Pigeon shows the person who asked a 6-digit join code. They pass it to the person at the other machine, in person or by a call or chat, who types it in. From then on the two machines know each other, and later joins need only Allow or Deny.`

Facts (short list beside or under the steps):

- `Pigeon asks the people, never the agents. An agent does not see the join code.`
- `After the first join, each machine accepts only the other's pinned certificate.`
- `A machine reaches only the topics it was allowed to join. Either person ends it at any time.`
- `Pigeon does not change your firewall or router, and there is no relay on the internet. Keep the listener off public networks.`

- Status line, visually part of the section and not a footnote: `Tried so far between two Windows machines on a home Wi-Fi network. Pigeon also listens on Tailscale addresses, but Tailscale, macOS and Linux have not been tried between machines yet.`
- Link: `How this is secured, and its limits` (`/Pigeon/security/#between-machines`)

**Visual 1:** the diagram from `docs/remote-design.md`, redrawn.

```text
machine A                                          machine B
agent ── service A ════ mutual TLS ════ service B ── agent
```

- Alt text: `Diagram: an agent and its Pigeon service on machine A, connected by a mutual TLS link to the Pigeon service and agent on machine B.`

**Visual 2:** a screenshot of Pigeon's own small window showing a join code, to be captured by the owner on Windows, where it has been used. A real window shows better than a description that the code appears outside the conversation. Use a made-up machine name and blur nothing that is not there.

- Caption: `The join code appears in the agent app or in a small Pigeon window, outside the conversation.`
- Alt text: `A small Pigeon window showing a 6-digit join code and the instruction to give it to the person at the other machine.`

**Snippet** (format from the CLI source; confirm at capture):

```console
$ pigeon peer listen
Other machines can reach "dev-laptop" at 192.168.2.20:29374. Allow that port in the firewall if your system asks; Pigeon does not change it.
Someone on another machine joins one of your topics as <topic>@192.168.2.20 the first time.
```

#### H9. Where the beta stands

**Job:** the honest status, placed after the reader understands the product and directly before the install steps. Two columns of equal visual weight, then four short points. Neutral presentation: no warning colors, no alarm icons.

**Copy:**

- Section id: `beta`
- Headline: `Where the beta stands`
- Intro: `Pigeon 0.7.0-beta.2 is an exploratory beta. This is what has been tried and what has not.`

Column 1, title `Tried`:

- `Messaging, wake and recovery with Claude and Codex on Windows.`
- `Antigravity, OpenCode 2 and Copilot on Windows, with development copies of their plugins.`
- `Two Windows machines on a home Wi-Fi network.`
- `Installers installed and checked automatically on Windows x64, Apple Silicon and Ubuntu x64.`
- `Automated tests on Windows, Ubuntu and macOS for every build.`

Column 2, title `Not yet tried`:

- `Any agent tool on macOS or Linux.`
- `The Intel Mac, Linux ARM64 and RPM packages on their own systems. They are built but not run by the release checks.`
- `Machines other than Windows talking to each other, and Tailscale.`
- `Copilot CLI and Antigravity CLI conversations.`

Sub-headline: `Four things to know before you install`

1. **`The installers are not signed yet.`** `Windows and macOS ask you to confirm them, and the install page shows how. Each release lists SHA-256 checksums, which detect a changed download but are not a signature.`
2. **`There are no passwords on your computer.`** `The Pigeon service does not check who is calling it. Any program on the computer, under any account, can read Pigeon's messages or send as one of your sessions. Use a computer that only you use, and do not send secrets through Pigeon.`
3. **`Other machines only on a trusted private network.`** `The network listener is off until you turn it on. Do not forward its port on a router.`
4. **`Use test conversations for now.`** `Try the beta with new conversations and made-up messages, not real work.`

- Links: `Security and limits` (`/Pigeon/security/`), `Known issues` (`[KNOWN_ISSUES]`)

#### H10. Install in three steps

**Job:** convert. Show the whole path at a glance and hand over to the Install page.

**Copy:**

- Section id: `install`
- Headline: `Install in three steps`

1. Title: `Install the pigeon command` Text: `One installer for Windows, macOS or Linux. It needs no .NET, and no administrator rights on Windows.`
2. Title: `Start the service`

```shell
pigeon start
```

3. Title: `Add the plugin to your agent tools` Text: `For Claude Code:`

```text
/plugin install pigeon --marketplace AndreVianna/Pigeon
```

Text after the snippet: `Codex, Antigravity, OpenCode and Copilot each have their own plugin.`

- Primary button: `Download the beta` (`/Pigeon/install/`)
- Text link: `All files for 0.7.0-beta.2 on GitHub` (`[RELEASE]`)
- Small line: `Already installed? Run pigeon update --allow-pre-release to get the newest beta.`

#### H11. Help test

**Job:** the beta's actual goal. Make reporting feel small and make partial results welcome.

**Copy:**

- Section id: `help-test`
- Headline: `Tell us what happened, especially on Mac and Linux`
- Body: `The beta guide has six short tests for one machine and six optional ones for two. Run the ones your setup allows and report each as pass, fail or not run. A failed install is a useful report. So is "the message arrived only after I asked the agent to check".`
- Primary button: `Report your results` (`[REPORT]`)
- Secondary button: `Open the beta guide` (`[BETA_GUIDE]`)
- Text link: `Star Pigeon on GitHub` (`[REPO]`)
- Small line: `Reports are public GitHub issues. Use made-up messages and remove machine names, addresses, join codes and session IDs.`

#### H12. Questions

**Job:** answer the remaining objections in the reader's words. An accordion is fine; all answers must be in the page source.

**Copy:**

- Section id: `faq`
- Headline: `Questions`

**`Does Pigeon need an account or a cloud service?`**
`No. Pigeon runs on your computer and keeps its messages in a local database. It uses the network in two cases only: when you run pigeon update, which downloads from GitHub, and when you turn on the listener for other machines.`

**`Does it start new agents?`**
`No. Pigeon connects two conversations that are already open and, where the tool supports it, wakes them when a message arrives. It does not launch or replace an agent.`

**`Can the other agent make mine run commands?`**
`A message is delivered as information from a peer, and Pigeon's plugins tell each agent to treat it that way. It grants no permission: your tool's own rules still apply to whatever your agent does next. That also means an agent you have allowed to run commands without asking can act on what a message says, so give two connected agents the permissions you would give either one alone.`

**`What happens when a conversation is closed?`**
`It cannot be woken. Its messages wait for up to 24 hours, and when you reopen the conversation the plugin restores its topics. If both sessions stay disconnected for an hour, the topic and its waiting messages are deleted.`

**`Can three agents share a topic?`**
`No. A topic holds two sessions. To bring in a third agent, use a second topic.`

**`Does Pigeon use my model's tokens?`**
`Each wake is a turn in your agent, like a message you typed. In addition, an idle Claude conversation with a joined topic gets a short renewal turn every 12 hours so that it stays reachable; that turn is not a message and does nothing else.`

**`Do I need .NET?`**
`Not for the installers. They include what Pigeon needs. Building from source needs the .NET 11 SDK.`

**`Does the service start with my computer?`**
`No. Run pigeon start after a restart or after logging out. Waiting messages are kept while the service is stopped.`

**`What does it cost?`**
`Nothing. Pigeon is open source under the MIT License.`

#### H13. Footer

**Copy:**

- Line 1: `Pigeon is open source under the MIT License. Made by Andre Vianna.`
- Links: `GitHub` (`[REPO]`), `Releases` (`[RELEASE]`), `Changelog` (`[CHANGELOG]`), `Beta guide` (`[BETA_GUIDE]`), `Known issues` (`[KNOWN_ISSUES]`), `Security and limits` (`/Pigeon/security/`), `Report a beta result` (`[REPORT]`), `Suggest a feature` (`[FEATURE]`)
- Line 2: `This site sets no cookies and runs no analytics.`
- Line 3: `Pigeon is an independent project. Claude, Codex, ChatGPT, Antigravity, OpenCode and GitHub Copilot are products of their respective owners.`

---

### Install

**Page title (`<title>`):** `Install Pigeon 0.7.0-beta.2 on Windows, macOS or Linux`

**Meta description:** `Download the Pigeon beta for Windows, macOS or Linux, start the service, add the plugin to your agent tools, and connect two agents.`

#### I1. Title and "before you start"

**Copy:**

- Eyebrow: `Public beta`
- Headline: `Install Pigeon 0.7.0-beta.2`
- Intro: `Four steps: install the command, start the service, add the plugin, connect two agents.`

Box, title `Before you start`:

- `Close your agent apps (Claude, Codex, Antigravity, OpenCode, Copilot). Reopen them after installing so they find the pigeon command.`
- `Use a computer that only you use. Pigeon's local service does not check who is calling it.`
- `Use new test conversations and made-up messages for this beta, not real work or secrets.`

Line under the box: `You need Windows 11 (or a supported Windows 10 Enterprise or LTSC edition), macOS, or Linux, and at least one of the agent tools in step 3.`

#### I2. Step 1: download and install

**Job:** the right file and the exact confirmation steps. Tabs by operating system; each tab is complete on its own.

**Copy:**

- Headline: `1. Install the pigeon command`
- Intro: `Each file is about 12 MB and includes everything Pigeon needs.`

**Tab `Windows`**

- Button: `Download for Windows (x64, .msi)` links to `[ASSETS]Pigeon-0.7.0-beta.2-win-x64.msi`
- Steps:
  1. `Run the .msi. It installs for you only and needs no administrator rights.`
  2. `The installer is not signed yet. If Windows shows "Windows protected your PC", choose More info, then Run anyway.`
  3. `Pigeon is installed to %LOCALAPPDATA%\Programs\Pigeon\pigeon.exe and added to your PATH.`
- Note: `If you use Claude Code, also install Git for Windows. The Claude plugin's hook runs under Git Bash.` Link: `https://git-scm.com/download/win`

**Tab `macOS`**

- Buttons: `Download for Apple Silicon (.pkg)` links to `[ASSETS]Pigeon-0.7.0-beta.2-osx-arm64.pkg`; `Download for Intel (.pkg)` links to `[ASSETS]Pigeon-0.7.0-beta.2-osx-x64.pkg`
- Steps:
  1. `Pick the package for your processor. The wrong one refuses to install.`
  2. `The package is not signed or notarized, so macOS may block it. If you decide to proceed, open System Settings > Privacy & Security and choose Open Anyway. Control-click does not bypass this on current macOS.`
  3. `Installing needs an administrator password. Pigeon is installed to /usr/local/bin/pigeon.`
- Note: `The Intel package is built but has not been run on an Intel Mac by the release checks. If you try it, please report the result.`

**Tab `Linux`**

- File list:

| System | File | Install with |
|---|---|---|
| `Debian, Ubuntu (x64)` | `pigeon_0.7.0.beta.2_amd64.deb` | `sudo apt install ./pigeon_0.7.0.beta.2_amd64.deb` |
| `Debian, Ubuntu (ARM64)` | `pigeon_0.7.0.beta.2_arm64.deb` | `sudo apt install ./pigeon_0.7.0.beta.2_arm64.deb` |
| `Fedora, RHEL, openSUSE (x64)` | `pigeon-0.7.0-0.beta.2.x86_64.rpm` | `sudo dnf install ./pigeon-0.7.0-0.beta.2.x86_64.rpm` |
| `Fedora, RHEL, openSUSE (ARM64)` | `pigeon-0.7.0-0.beta.2.aarch64.rpm` | `sudo dnf install ./pigeon-0.7.0-0.beta.2.aarch64.rpm` |
| `Other Linux (x64)` | `pigeon-0.7.0-beta.2-linux-x64.tar.gz` | `Unpack it and put pigeon in a folder on your PATH.` |
| `Other Linux (ARM64)` | `pigeon-0.7.0-beta.2-linux-arm64.tar.gz` | `Unpack it and put pigeon in a folder on your PATH.` |

Each file name links to `[ASSETS]` plus that name. On openSUSE, `zypper install` replaces `dnf install`.

- Notes:
  - `The .deb and .rpm install to /usr/bin/pigeon.`
  - `Pigeon needs the SQLite library (libsqlite3), which nearly every distribution includes.`
  - `Install zenity (GNOME and most desktops) or kdialog (KDE) so Pigeon can show its approval windows. Without them, approvals are answered in a terminal with pigeon requests.`
  - `The ARM64 and RPM packages are built but have not been run on their systems by the release checks. If you try one, please report the result.`

**Under the tabs, all systems.** Title: `Check your download (optional)`

- Text: `The release includes SHA256SUMS.txt. Compare your file's SHA-256 with its line there. A checksum detects a changed download; it is not a code signature.`
- Link: `SHA256SUMS.txt` (`[ASSETS]SHA256SUMS.txt`)
- Snippets, one per system:

```powershell
Get-FileHash .\Pigeon-0.7.0-beta.2-win-x64.msi
```

```shell
shasum -a 256 Pigeon-0.7.0-beta.2-osx-arm64.pkg
```

```shell
sha256sum pigeon_0.7.0.beta.2_amd64.deb
```

- Small line: `Had Pigeon as a .NET tool before? Remove it first: pigeon stop, then dotnet tool uninstall --global Pigeon.Cli.`

#### I3. Step 2: start the service

**Copy:**

- Headline: `2. Start the service`
- Text: `Open a new terminal and run:`

```console
$ pigeon --version
0.7.0-beta.2

$ pigeon start
Pigeon service started.

$ pigeon status
Pigeon service: running, version 0.7.0-beta.2
Connected sessions: 0 (and 0 agent connections not in a topic)
Sessions with waiting messages: 0
```

The output lines follow the CLI source; confirm at capture on 0.7.0-beta.2.

- Text after: `The service runs in the background until you stop it or restart the computer. It does not start by itself after a restart; run pigeon start again.`

#### I4. Step 3: add the plugin

**Job:** per-tool instructions. Tabs by tool. State once, above the tabs, why a plugin and not a bare MCP server.

**Copy:**

- Headline: `3. Add the plugin to your agent tools`
- Intro: `Install the plugin in each tool you want to connect, then reopen the tool. Install the plugin, not only an MCP server: the plugin also brings the hooks that wake the agent and the connect skill.`
- Note above the tabs: `Claude and Codex install the plugin straight from GitHub. The Antigravity, OpenCode and Copilot plugins are installed from a copy of the repository, so get one first:`

```shell
git clone https://github.com/AndreVianna/Pigeon.git
cd Pigeon
```

**Tab `Claude Code / Claude Desktop`**

- Text: `In Claude Code, run:`

```text
/plugin install pigeon --marketplace AndreVianna/Pigeon
```

- Text: `In Claude Desktop, add AndreVianna/Pigeon as a marketplace in the plugin settings and install pigeon.`

**Tab `Codex / ChatGPT desktop app`**

```shell
codex plugin marketplace add AndreVianna/Pigeon
```

- Text: `Or add the repository in the app's Marketplace tab. Then install pigeon and approve its hooks when asked. If you added Pigeon earlier as a plain MCP server, remove that server first.`

**Tab `Antigravity App`**

- Text: `Needs the Antigravity App 2.28 or later. From your copy of the Pigeon repository, run:`

```shell
agy plugin install ./integrations/antigravity-plugin
```

- Text: `Install it globally; Antigravity runs a plugin's background helper only from the global plugin folder. Then turn on Pigeon wake in the Automations dashboard, which is what wakes an idle conversation, and allow the Pigeon tools. Antigravity asks once for each tool, so the first wake stops until you have allowed pigeon_receive, pigeon_send and pigeon_wake_complete.`

**Tab `OpenCode 2`**

- Text: `From your copy of the Pigeon repository, copy the folder integrations/opencode-plugin/pigeon to ~/.config/opencode/plugins/pigeon for all projects, or to .opencode/plugins/pigeon in one project. Then restart OpenCode.`
- Text: `The plugin needs OpenCode's server running, which the CLI and Desktop start. OpenCode 1.x is not supported.`

**Tab `GitHub Copilot`**

- Text: `For the Copilot app, Copilot CLI, and VS Code chats with Copilot as the Session Target. From your copy of the Pigeon repository, run:`

```shell
copilot plugin install ./integrations/copilot-plugin
```

- Text: `The plugin allows Pigeon's own tools without asking; every other tool still asks. VS Code's older Local chats are not woken.`

#### I5. Step 4: connect two agents

**Copy:**

- Headline: `4. Connect two agents`

1. `In the first agent, connect to a topic. In Claude, type /pigeon:connect parser-review. In the other tools, ask: "Connect to Pigeon topic parser-review." The agent reports that it is waiting for a partner.`
2. `Do the same in the second agent with the same topic name.`
3. `Ask either agent to send something to the other, for example: "Ask Codex to review the parser changes."`

- Text after: `From then on, each message wakes the other agent while its conversation is open. Run pigeon status to see which sessions are connected and what is waiting.`

#### I6. Check it without an agent

**Job:** separate a Pigeon problem from an agent-tool problem. This is test T0 of the beta guide.

**Copy:**

- Headline: `Check Pigeon without an agent`
- Text: `These six commands use two stand-in sessions. If the receive command prints your message, the service and its message store work. This does not test your agent tools.`

```shell
pigeon join beta-cli-check as beta-cli-a using codex
pigeon join beta-cli-check as beta-cli-b using claude-code
pigeon send from beta-cli-a to beta-cli-check "PIGEON_BETA_CLI_01"
pigeon receive for beta-cli-b from beta-cli-check
pigeon leave beta-cli-check as beta-cli-a
pigeon leave beta-cli-check as beta-cli-b
```

- Text after: `The receive command should print a line containing PIGEON_BETA_CLI_01.`

#### I7. Update and uninstall

**Copy:**

- Headline: `Update`
- Text: `Close your agent apps, then run:`

```shell
pigeon update --allow-pre-release
```

- Text: `It downloads the newest release's installer for your system, checks it against SHA256SUMS.txt, installs it the way you installed Pigeon, and restarts a running service. On macOS and Linux it asks for your password through sudo. The flag matters during the beta: without it, pigeon update looks only at stable versions and finds nothing newer.`
- Small line: `Version 0.7.0-beta.1 has no update command. Install 0.7.0-beta.2 with its installer once. After updating, update the plugins in your agent tools as well; they are separate from the command.`

- Headline: `Uninstall`
- `Windows: Settings > Apps > Installed apps > Pigeon > Uninstall.`
- `macOS: run pigeon stop, then sudo rm /usr/local/bin/pigeon and sudo pkgutil --forget io.github.andrevianna.pigeon.`
- `Linux: run pigeon stop, then sudo apt remove pigeon or sudo dnf remove pigeon.`
- Text: `Messages and the machine's identity stay in Pigeon's data folder until you delete it: %LOCALAPPDATA%\Pigeon on Windows, ~/Library/Application Support/Pigeon on macOS, ~/.local/share/Pigeon on Linux.`

#### I8. If something goes wrong

**Copy:**

- Headline: `If something goes wrong`

| Title | Text |
|---|---|
| `An agent app cannot find pigeon` | `Close the app completely and open it again so it reads the new PATH. Desktop apps started from the macOS Dock or a Linux launcher may see a different PATH from your shell. If it still fails, report which installer and which app.` |
| `The installer says Pigeon is in use` | `An agent app's Pigeon plugin keeps the program running. Close your agent apps and try again.` |
| `Nothing wakes after a restart` | `The service does not start with the computer. Run pigeon start, then check pigeon status.` |
| `The agent got the message only when asked` | `That is polling, not a wake. Check pigeon status for that session's notification line, and please report it: this is what the beta is trying to find.` |

- Link: `Known issues` (`[KNOWN_ISSUES]`)

#### I9. Report your results

**Copy:**

- Headline: `Now tell us what happened`
- Body: `Run the tests in the beta guide that your setup allows, and report each as pass, fail or not run. Partial results and failed installs help.`
- Primary button: `Report your results` (`[REPORT]`)
- Secondary button: `Open the beta guide` (`[BETA_GUIDE]`)

---

### Security and limits

**Page title (`<title>`):** `Security and limits of the Pigeon beta`

**Meta description:** `What the Pigeon beta protects and what it does not: no local sign-in, unsigned installers, and links between machines on trusted private networks only.`

#### S1. Title and summary

**Copy:**

- Headline: `Security and limits`
- Intro: `Pigeon is a beta built for a developer's own computer. This page says what it protects, what it does not, and what that means for how you use it.`

Summary list, title `In short`:

- `On your computer, Pigeon does not check who is calling it. Use it on a computer that only you use.`
- `A message between agents is information. It cannot grant a permission.`
- `Connections to other machines are off by default, need a person's approval for every join, and belong only on a trusted private network.`
- `The installers are not signed yet.`
- `Messages are stored unencrypted on your disk and can occasionally arrive twice. Do not send secrets through Pigeon.`

#### S2. On your computer

**Copy:**

- Section id: `local`
- Headline: `On your computer: no passwords`
- Body: `The Pigeon service listens on 127.0.0.1 only, so it is not reachable from the network. It has no authentication. A topic name and a session ID are labels that route messages; they do not prove who is sending.`
- Body: `Any program running on the computer, under any user account, can join a topic, use any session ID, read and send messages, answer a join request from another machine, or stop the service. The service rejects requests that come from a web page in a browser, which reduces one kind of attack but does not identify the caller.`
- Title: `What to do`
- List: `Run Pigeon on a computer that only you use.` / `Do not send passwords, keys or other secrets through Pigeon.` / `Do not expose the local port (29372) to the network.`
- Small line: `Authenticating local callers is planned for a later version.`
- Link: `Local trust boundaries, in full` (`[LOCAL_TRUST]`)

#### S3. What one agent can do to another

**Copy:**

- Section id: `agents`
- Headline: `Between agents: messages are data`
- Body: `Pigeon delivers a message as information from a peer, and its plugins instruct each agent to treat it that way. A message grants nothing: it does not answer your tool's permission prompts, change its sandbox, or authorize a shell command. What your agent does after reading a message is governed by the permissions you gave it in its own tool.`
- Body: `This is a rule the agents are told to follow, not a filter. Pigeon does not inspect messages, and an agent that you already allow to run commands without asking can act on what a message says. Treat a message from the other agent as you would treat any text your agent reads.`
- Body: `Pigeon also does not reserve files, so two agents can still edit the same file at once.`
- Body: `To keep two agents from answering each other without end, a topic accepts at most 60 messages an hour.`

#### S4. Between machines

**Copy:**

- Section id: `between-machines`
- Headline: `Between machines: a trusted private network only`
- Body: `Sharing a topic with another machine is optional. Nothing is reachable from another machine until a person runs pigeon peer listen, an agent opens a topic to other machines at its person's request, and a person approves the join.`

Title: `How a connection is made`

- `Each Pigeon service has its own key and certificate, created on first use. The private key never leaves your machine.`
- `The two services talk over TLS and each must present its certificate. After the first join, each side accepts only the certificate it pinned.`
- `The first join between two machines uses a 6-digit code. Pigeon shows it to the person who asked, outside the conversation; they give it to the person at the other machine, who types it in. Three wrong codes or ten minutes without an answer refuse the join.`
- `Later joins from a known machine ask that person for Allow or Deny.`
- `Agents see only the outcome. The code never appears in anything an agent can read through Pigeon.`

Title: `What another machine can and cannot do`

- `A known machine can see the topics you opened to other machines, ask to join one, exchange messages in the topics it joined, and leave.`
- `It cannot join without a person's answer, reach a topic you did not open, see your session IDs, read your status, or stop your service.`
- `Either person can end a topic or forget the other machine at any time.`

Title: `Limits we accept in this beta`

- `Another account on your computer can use your Pigeon service, including its topics with other machines. Use this on single-user machines.`
- `On the first contact only the machine that opened the topic checks the code. An active attacker already on your network who takes that machine's address could pose as it.`
- `An agent that can run shell commands could run Pigeon's approval commands, and an agent that can control your screen could click an approval window. Pigeon does not try to prevent that.`
- `When one of the machines is a Mac, the link uses TLS 1.2, and someone on the network can tell which two machines are talking, though not what they say.`
- `Both machines need to accept an incoming connection on port 29374. Managed corporate devices and guest Wi-Fi often block that.`
- `There is no relay and no discovery. The first join names the other machine's address.`

- Status line: `Tried so far between two Windows machines on a home Wi-Fi network. Not yet tried: macOS, Linux and Tailscale.`
- Link: `Remote connections design, with threats and mitigations` (`[REMOTE_DESIGN]`)

#### S5. Installers and updates

**Copy:**

- Section id: `installers`
- Headline: `Installers and updates`
- Body: `The Windows installer and the macOS packages are not signed, so Windows SmartScreen warns about an unknown publisher and macOS Gatekeeper blocks the package until you allow it. Signing needs certificates the project does not have yet.`
- Body: `Every release includes SHA256SUMS.txt. A matching checksum shows that your download is the file that was published. It does not prove who published it.`
- Body: `pigeon update downloads from this project's GitHub releases, refuses a file that does not match its line in SHA256SUMS.txt, and never installs an older version.`
- Link: `How to install` (`/Pigeon/install/`)

#### S6. What is stored

**Copy:**

- Section id: `data`
- Headline: `What is stored, and where`
- Body: `Messages are kept in a SQLite database in your user profile: %LOCALAPPDATA%\Pigeon\messages.db on Windows, ~/Library/Application Support/Pigeon/messages.db on macOS, ~/.local/share/Pigeon/messages.db on Linux. The database is not encrypted.`
- Body: `You can have the text of read or expired messages erased after a number of days with the PIGEON_BODY_RETENTION_DAYS setting. This is not secure erasure: database files and backups can still contain the text.`
- Body: `A topic shared with another machine keeps its messages on both machines.`
- Body: `Pigeon sends nothing to its author or to any service. The site you are reading sets no cookies and runs no analytics.`

Owner: the last sentence rests on a search of the source for outbound addresses (loopback, approved peers, and GitHub for `pigeon update`). Confirm before publishing.

#### S7. Delivery limits

**Copy:**

- Section id: `delivery`
- Headline: `Delivery limits`
- List:
  - `Messages are held for up to 24 hours.`
  - `Delivery is at least once. If an agent stops while reading, the same message is delivered again.`
  - `A message that is stored is not yet a message that was read. Pigeon records an acknowledgment separately, when the agent finishes handling it.`
  - `A topic whose two members have both been disconnected for an hour is deleted, with any messages still waiting.`
  - `A closed conversation cannot be woken.`
- Link: `Known issues` (`[KNOWN_ISSUES]`)

#### S8. Report a security problem

**Copy (interim, see open question 9):**

- Section id: `report`
- Headline: `Report a security problem`
- Body: `GitHub issues are public. For a problem that should not be public yet, open an issue that says only that you have a security report, without the details, and the maintainer will arrange a private channel.`
- Button: `Open an issue` (`[FEATURE]`)

If the owner turns on GitHub's private vulnerability reporting, replace the body with: `Use GitHub's private report form. Only the maintainer sees it.` and point the button, labeled `Report privately`, to `https://github.com/AndreVianna/Pigeon/security/advisories/new`.

---

### Not found (404)

**Page title (`<title>`):** `Page not found · Pigeon`

**Meta:** no description; add `<meta name="robots" content="noindex">`.

**Copy:**

- Headline: `This page is not here`
- Body: `The address may be wrong, or the page may have moved.`
- Button: `Go to the home page` (`/Pigeon/`)
- Text link: `Install Pigeon` (`/Pigeon/install/`)

---

## 5. Calls to action

One primary action across the site, so every screen answers "what do I do next" the same way.

| Action | Label | Destination | Where it appears |
|---|---|---|---|
| **Primary: download the beta** | `Download the beta` | `/Pigeon/install/` | Header button on every page (H1), hero (H2), install steps (H10) |
| Primary, on the Install page | `Download for Windows (x64, .msi)` and the per-system equivalents | Direct file under `[ASSETS]` | Install step 1 (I2) |
| **Secondary: beta guide** | `Read the beta guide` in the hero, `Open the beta guide` elsewhere | `[BETA_GUIDE]` | Hero (H2), help test (H11), Install closing section (I9), footer |
| **Secondary: report results** | `Report your results` | `[REPORT]` | Help test (H11) as that section's main button, Install closing section (I9), footer, announcement bar through `#help-test` |
| **Tertiary: star the repository** | `Star Pigeon on GitHub` | `[REPO]` | Help test (H11) as a text link only. Do not show a star count while it is near zero. |
| Tertiary: source | `GitHub` | `[REPO]` | Header navigation, footer |
| Tertiary: feature request | `Suggest a feature` | `[FEATURE]` | Footer |

Rules:

- The primary button goes to the Install page, not to a file. The reader must pass the "before you start" box and the unsigned-installer steps on the way to the download.
- The report action is the main button only where the reader has already installed or is being asked to test (H11, I9). Elsewhere it would compete with the download.
- One primary-styled button per section.

---

## 6. Trust and honesty

The page must say four things. Each is stated once in short form where the reader makes the related decision, and once in full on the Security page. None of them appears as a banner, a modal or a red warning: this audience reads plain statements of limits as competence.

| What must be said | Short form, and where | Full form |
|---|---|---|
| **It is a beta, tried with agents on Windows only.** | Announcement bar (H0) and hero eyebrow (H2): "Public beta". "Tried so far" column in the tool table (H6). The tried and not-yet-tried lists (H9). | H9 is the full form; `[KNOWN_ISSUES]` for detail. |
| **The installers are not signed.** | First of the four points in H9. Then inside the Windows and macOS tabs at the step where the system warning appears (I2), with the exact buttons to press. | S5. |
| **The local service does not authenticate callers.** | Second of the four points in H9, as "There are no passwords on your computer". "Before you start" box (I1). | S2. |
| **Network use only on trusted private networks.** | In the headline and body of H8 itself, plus the "optional, off by default" eyebrow and the status line. Third of the four points in H9. | S4. |

Placement principles:

- **After understanding, before commitment.** The hero carries only "Public beta". The limits come in H9, when the reader knows what the product is and is about to install.
- **At the point of action.** The SmartScreen and Gatekeeper steps sit in the install flow where the warning appears, so the warning is expected and not alarming.
- **Paired with what to do.** Each limit comes with an instruction: use a computer only you use, use made-up messages, keep the listener off public networks.
- **Same weight as the claims.** "Tried" and "Not yet tried" are two equal columns. The status line in H8 is body text, not a footnote.
- **No softening and no dramatizing.** Do not write "may have minor issues". Do not write "use at your own risk". State the fact.

Things the page must not claim: exactly-once delivery; a wake after every reopen; that Pigeon prevents an agent from acting on a message; that Pigeon has been tried on macOS, Linux or Tailscale; that the network link hides which machines are talking when a Mac is involved; that another local account is kept out.

---

## 7. Success measures and open questions

### What the site is for

The beta's goal, from `docs/beta-launch-plan.md`, is usable reports from people running AI coding agents, especially on macOS and Linux. Views and votes are not the goal. The site succeeds when a visitor who has the problem installs the beta and files a report.

### Measures a static page can support

No analytics on the site. GitHub already records the steps that matter, without cookies or a consent banner.

| Step | Measure | Baseline, 2026-11-24 |
|---|---|---|
| Reach | Visits to the repository that came from the site: look for `andrevianna.github.io` among the referrers. GitHub keeps 14 days, so read it every two weeks. | Site not live |
| Download | Downloads per installer file | 1 per file; treat as zero |
| Report | Issues with the label `beta report`, and how many name macOS or Linux | 0 issues |
| Interest | Stars | 0 |

The commands that read them, in the same order:

```shell
gh api repos/AndreVianna/Pigeon/traffic/popular/referrers
gh release view v0.7.0-beta.2 --json assets --jq '.assets[] | "\(.name) \(.downloadCount)"'
gh issue list --label "beta report" --state all
gh repo view --json stargazerCount
```

Notes on reading them:

- `pigeon update` downloads installers too, so from the next beta on, download counts include updates by existing testers. Compare first-week downloads between betas, not totals.
- The per-system split of downloads is itself a result: it shows whether Mac and Linux users arrive at all.
- The ratio of reports to downloads shows where people stop. Many downloads with no reports points at the install or plugin step, or at the report form.

Proposed targets for the first 30 days after the site goes live, for the owner to adjust: 50 installer downloads across all files; 5 beta reports, with at least 2 from macOS and 2 from Linux; the site among the repository's top five referrers.

Two small changes outside the site would make these measures more useful: an optional "Where did you hear about Pigeon?" field on the beta report form, and setting the repository's About description and homepage URL (both are empty) so that visitors arriving at GitHub first can find the site.

If the owner later wants page-level numbers, the least intrusive option is a cookieless, self-hosted or privacy-focused counter. This brief does not recommend one now: the audience is small, the three GitHub measures cover the funnel, and "no analytics" is a statement this audience values.

### Open questions for the owner

1. **Where the site lives in the repository.** GitHub Pages is not enabled yet. `docs/` already holds internal Markdown (plans, research, history) that should not be published as the site. Proposed: a `site/` folder deployed by a GitHub Actions workflow. Confirm.
2. **Who updates the site on a release.** The version appears in the announcement bar, the hero line, the Install page (title, headline, nine file links, three checksum commands, the `pigeon --version` output) and the links table. Proposed: add "update the site" to the release steps, or generate these values from one data file. Which?
3. **Documentation links: release tag or `main`?** This brief pins them to `v0.7.0-beta.2`, as the release notes do, so the guide matches the installed version. Pinning means editing them on every release. Confirm.
4. **The recording.** The hero is designed for a real recording of two sessions (H2, version B). Can the owner capture it on Windows with Claude Code and Codex? Until then the hero uses the command-line transcript and H7 is left out.
5. **Captures to confirm.** The local `pigeon` on the machine where this brief was written is 0.6.0-alpha.8, so no output was captured from 0.7.0-beta.2. Please run and paste the real output of `pigeon start`, `pigeon status` (fresh install), `pigeon topics` and `pigeon peer listen` to replace the snippets marked "confirm at capture", and capture the join-code window for H8.
6. **Which copy of the repository testers clone for the Antigravity, OpenCode and Copilot plugins.** The Install page shows a plain `git clone`. Should it name the release tag, since plugins and the command share a version?
7. **The changelog.** `CHANGELOG.md` still marks 0.7.0-beta.2 as "unreleased", although it was published on 2026-11-23. The footer links to it.
8. **"No telemetry" statement.** S6 and the footer say Pigeon and the site send nothing to anyone. The claim about Pigeon rests on a source search, not on a documented statement. Confirm, and consider adding the sentence to the README.
9. **Security contact.** There is no security policy file and private vulnerability reporting is not confirmed to be on. Turn it on, or give another private contact, so S8 can drop the interim wording.
10. **Tool names and the independence line.** The footer says Pigeon is independent of the tool makers and the site uses their names as text without logos. Confirm that wording.
11. **Hero headline.** "Stop being the messenger between your AI agents." reuses the line from the LinkedIn infographic. An alternative that names the tools: "Let Claude Code and Codex talk to each other." This brief chose the first because it also covers the other three tools.
12. **Fonts.** Hosting Inter and JetBrains Mono with the site, not loading them from Google Fonts, keeps the "no third parties" statement true. Confirm that the designer may do this.
13. **Custom domain.** Not needed now. Some listings (BetaList) require one. Decide later.
