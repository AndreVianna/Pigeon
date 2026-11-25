# Pigeon website: product brief and page copy, version 2

Prepared 2026-11-24 for Pigeon 0.7.0-beta.2. This version replaces the structure and copy of `product-brief.md` (version 1), which stays in the folder unchanged. The owner found version 1 too dry and asked for a home page in four layers:

1. **Hook.** One image-led idea a non-technical person gets in a glance, with an animation and very few words.
2. **How it works, for a newcomer.** Short, plain panels with no jargon.
3. **Get started.** Install Pigeon, add it to your tool, connect two agents. One line per item and a link to the detail.
4. **For engineers.** Everything else: capabilities, mechanism, limits, security, settings, commands, questions.

Every product claim comes from the repository at the `v0.7.0-beta.2` tag (`README.md`, `CHANGELOG.md`, `docs/architecture.md`, `docs/beta-testing.md`, `docs/known-issues.md`, `docs/security-design.md`, `docs/remote-design.md`, the CLI source) and the published release. Snippets that could not be captured from a running 0.7.0-beta.2 are marked "confirm at capture".

## How to read this file

- Text in a "Copy" block is final. Text in a "Job", "Visual" or "Layout" line is direction, not copy.
- Copy strings are wrapped in backticks only to mark where they start and end. Inside them, set commands (such as pigeon start), file names, paths and topic names in monospace.
- Named links such as `[RELEASE]` are defined in "Links and facts".

## What changed from version 1

| Area | Version 1 | Version 2 |
|---|---|---|
| Home structure | 13 sections in one register | Four layers that go from a picture, to plain words, to setup, to reference |
| First screen | Headline, paragraph, two buttons, a terminal transcript, an announcement bar | An animation, a headline, one line, one button |
| Hero headline | "Stop being the messenger between your AI agents." | "Let your AI agents talk to each other." The version 1 line assumes the reader already has the problem; the new one states the idea. |
| Beta wording | Announcement bar, hero eyebrow, a full section before install | One short line beside the download links, and the full account in layer 4 |
| "How it works" | Written for developers, with a diagram of plugins and a service | Four plain panels. The technical account moves to layer 4. |
| Install on Home | Three steps and a button to the Install page | Download links per system and one card per tool on Home, each linking to its detailed steps |
| Download button | Went to the Install page | Scrolls to "Get started" on Home, where the download links and the beta line are |
| Security page | Its own page | Folded into layer 4 of Home. The site is now Home, Install and a 404. |
| Terminal transcript | In the hero | In layer 4, with the command reference |
| Added | | A settings table and a command reference (built from the CLI's own help, not the README table, which lists removed commands), a limits table with numbers, a "Cursor and other tools: not supported yet" note |
| Dropped | | The announcement bar, the hero's second button and status line, the separate "The problem" section (the animation and the line under the headline carry it), the navigation links to "Works with" and "Beta status" |

## Links and facts

| Name | Value |
|---|---|
| Version | `0.7.0-beta.2` (prerelease, published 2026-11-23) |
| `[SITE]` | `https://andrevianna.github.io/Pigeon/` |
| `[REPO]` | `https://github.com/AndreVianna/Pigeon` |
| `[RELEASE]` | `https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.2` |
| `[ASSETS]` | `https://github.com/AndreVianna/Pigeon/releases/download/v0.7.0-beta.2/` followed by a file name |
| `[BETA_GUIDE]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/beta-testing.md` |
| `[KNOWN_ISSUES]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/known-issues.md` |
| `[ARCHITECTURE]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/architecture.md` |
| `[REMOTE_DESIGN]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/remote-design.md` |
| `[LOCAL_TRUST]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/docs/security-design.md` |
| `[README]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/README.md` |
| `[CHANGELOG]` | `https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.2/CHANGELOG.md` |
| `[REPORT]` | `https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml` |
| `[FEATURE]` | `https://github.com/AndreVianna/Pigeon/issues/new/choose` |
| `[LICENSE]` | `https://github.com/AndreVianna/Pigeon/blob/main/LICENSE` |

Two traps, as in version 1:

- Do not link to `/releases/latest`. GitHub's "Latest" release is 0.5.0, which is older than the beta and ships only a .NET tool package, with no installers.
- The asset names are not uniform. Use the exact names given in the copy.

A new release changes this table and every place the version or a file name is written out: the Home download links, the Install page (title, headline, file links, checksum commands, `pigeon --version` output), and the beta status in layer 4.

---

## 1. Audience

Unchanged from version 1, with one addition that shapes the first two layers.

- **Primary (unchanged):** a developer who already runs two AI coding agents on one project, typically Claude Code and Codex, and carries messages between them by hand. Mac and Linux users are the most wanted testers.
- **Secondary (unchanged):** people with two machines on one private network; evaluators such as listing editors and directory reviewers; returning testers.
- **New, for layers 1 and 2: the first-glance reader.** Someone who arrives from a post or a listing, may not write code every day, and decides in a few seconds whether this is interesting. They know what an AI assistant is. They do not know what MCP, a service or a session is. The hook and "How it works" are written so that this person can say what Pigeon does after one screen and explain how after the second.

What the reader must believe is also unchanged (it is about my existing conversations, it works with my tools, it takes minutes, the other agent does not get my permissions, the author says what does not work). The order of proof changes: the idea first, the proof and the limits later.

## 2. Positioning

- **One-sentence value proposition (unchanged):** Pigeon lets two AI coding sessions you already have open, in the same tool or in different ones, send each other messages that wait until they are read and wake the other session while its conversation is open and its tool allows it, so you stop carrying text between windows.
- **The same idea for the first-glance reader:** your AI agents can talk to each other, and Pigeon carries the messages.
- **Alternatives (unchanged, see version 1):** copy and paste, a shared handoff file, one tool's own subagents. They are not shown on the page in version 2; the line under the headline names the first one.

Writing rules for version 2:

- Layers 1 and 2 use no term that needs a definition. "Agent" is defined once in layer 2. "Topic" appears once there, with its plain meaning beside it. The words MCP, service, session, plugin, CLI, queue and hook do not appear before layer 3.
- Layer 3 introduces "plugin" with five words of explanation and "terminal" without one.
- Layer 4 is written for engineers and uses the product's real terms.
- The honesty rules of version 1 hold everywhere. "Wake" carries its condition wherever it is a promise in words (layer 2 onward). The animation shows a wake, and the panel that explains it says "as long as its chat is open".
- No hype words, no em-dash asides.

---

## 3. Page map

| Page | Path | Why it exists |
|---|---|---|
| **Home** | `/Pigeon/` | The four layers. A non-technical reader can stop after layer 2, a new user after layer 3, and an engineer finds the reference in layer 4. |
| **Install** | `/Pigeon/install/` | The detailed steps that Home links to: one anchor per operating system and one per tool. The README has all tool steps under a single "Install" heading with no per-tool anchors, so the cards on Home need this page as their target. |
| **Not found** | `/Pigeon/404.html` | GitHub Pages serves it for wrong paths. |

The Security page of version 1 is folded into layer 4 (`#security` and `#machines`). The beta guide, known issues and design documents stay on GitHub.

Site-wide notes for the designer, carried over from version 1:

- Internal links are relative or start with `/Pigeon/`.
- Terminal transcripts and commands are real text with a Copy button, not images.
- Tabs and accordions show everything when JavaScript is off. Scripts make no network request.
- No analytics, no cookies, no third-party embeds, no forms. Host the fonts with the site.
- Tool names are text. Do not use the logos of Claude, Codex, Antigravity, OpenCode or Copilot.

### Home: sections in order

| # | Layer | Section | Anchor | Job |
|---|---|---|---|---|
| H1 | | Header | | Wordmark, four links, the download button. |
| L1 | 1 | Hook | | Show two agents passing messages through Pigeon. One headline, one line, one button. |
| L2 | 2 | How it works | `#how` | Four plain panels that a newcomer can repeat to someone else. |
| L3 | 3 | Get started | `#get-started` | Install Pigeon, add it to your tools, connect two agents. |
| T1 | | Help test | `#help-test` | The beta's ask: report what happened. |
| L4 | 4 | For engineers | `#engineers` | The reference. Twelve scannable blocks, listed below. |
| F1 | | Footer | | License, author, links. |

Blocks of layer 4, in order:

| # | Block | Anchor |
|---|---|---|
| E1 | What Pigeon does (full list) | `#capabilities` |
| E2 | How it is built | `#architecture` |
| E3 | How a wake works | `#wake` |
| E4 | Supported tools | `#tools` |
| E5 | Other machines | `#machines` |
| E6 | Limits, with numbers | `#limits` |
| E7 | Where the beta stands | `#beta` |
| E8 | Security model | `#security` |
| E9 | Settings | `#settings` |
| E10 | Commands | `#commands` |
| E11 | Questions | `#faq` |
| E12 | Documents | `#docs` |

### Install: sections in order

Same as version 1 (I1 to I9), with anchors added so Home can link to one system or one tool. See "Install" under final copy.

---

## 4. Final copy

### Home

**Page title (`<title>`):** `Pigeon: let your AI agents talk to each other`

**Meta description:** `Pigeon carries messages between two AI coding agents, such as Claude Code and Codex, so you stop copying text between windows. Free, open source, public beta.`

**Social card:** `og:title` same as the title. `og:description`: `Your AI agents can message each other. Pigeon carries the messages, so you stop copying and pasting between windows.` `og:image`: a 1200 by 630 still of beat 3 of the hook animation (the message arriving and the second window lit), with the headline. Alt text: `Two agent windows with a message travelling from one to the other through the Pigeon mark.`

#### H1. Header

**Copy:**

- Wordmark: `Pigeon`
- Navigation: `How it works` (`#how`), `Get started` (`#get-started`), `For engineers` (`#engineers`), `GitHub` (`[REPO]`)
- Button: `Download the beta` (`#get-started`)

On narrow screens keep the wordmark and the button; the links go in a menu. The header is the same on every page. On the Install page and the 404, its anchors point back to Home: `/Pigeon/#how`, `/Pigeon/#get-started`, `/Pigeon/#engineers`.

#### L1. Hook

**Job:** in one screen, without reading more than twelve words, a person sees that two AI agents can pass messages to each other and that nobody has to carry them. The picture leads; the words label it.

**Layout:** the animation takes most of the first screen. Headline, one line and one button sit beside or under it. Nothing else is on the first screen: no announcement bar, no eyebrow, no second button, no badges.

**Copy:**

- Headline: `Let your AI agents talk to each other.`
- Line under it: `Pigeon carries messages between them, so you stop copying and pasting from one window to the other.`
- Button: `Download the beta` (`#get-started`)
- Small line under the button: `Free and open source · Windows, macOS and Linux`
- Scroll cue, a text link with a down arrow: `See how it works` (`#how`)

**Visual: the hook animation.** A looping illustration built from the site's own shapes (SVG and CSS, or a small script). It is an illustration of the idea, not a recording of the product, so the two windows are plain drawn panels with a text label each, not imitations of the real apps.

The scene: two agent windows, left and right, labeled `Claude Code` and `Codex` in text. Between them sits the Pigeon mark from `design/site/logo`, on a thin line that joins the two windows. The violet accent is used for the message and nothing else, so the eye follows it.

Storyboard, five beats, about 12 seconds in all:

| Beat | Time | What happens | What it says |
|---|---|---|---|
| 1. Ask | 0 to 2.5 s | Both windows are quiet. The right one is slightly dimmed. In the left window a message bubble writes itself: `Can you review the parser changes?` | One agent has something to say to the other. |
| 2. Carry | 2.5 to 4.5 s | The bubble folds into a small violet note, leaves the left window and travels along the line. As it passes the Pigeon mark, the mark's violet band brightens once. | Pigeon carries it. Nobody copies anything. |
| 3. Wake | 4.5 to 6.5 s | The note reaches the right window. The window comes up from dimmed to full brightness with a thin violet edge, and the note unfolds into the same message. | The other agent is woken and reads it. |
| 4. Reply | 6.5 to 9.5 s | The right window writes a short answer: `Done. Two notes for you.` It folds, travels back through the mark, and unfolds in the left window. | It works both ways. |
| 5. Rest | 9.5 to 12 s | Both windows are lit and still. A small caption fades in under the scene: `Nobody typed.` Hold, then everything fades back to beat 1. | The point, in two words. |

Motion rules:

- Calm. Ease in and out, no bounce, no overshoot, no parallax. Move only position and opacity. One thing moves at a time.
- A dimmed window means an open chat that is idle. Never draw it closed, minimized or missing: Pigeon cannot wake a closed chat.
- The loop restarts with a fade, not a jump. It pauses when the hook is scrolled out of view.
- A small `Pause` control (it becomes `Play`) sits at a corner of the scene, because the motion runs longer than five seconds.
- **Reduced motion:** when the visitor's system asks for reduced motion, show one still frame and no movement: both windows lit, the question shown in the right window and the answer in the left, the line through the Pigeon mark drawn in violet with a small arrowhead at each end, and the caption `Nobody typed.` The still frame is also what loads first, before any script runs, and what appears when JavaScript is off.
- On a phone the two windows stack and the note travels down and back up.
- The two messages are made-up examples. Keep them as real text in the page where possible.
- Text alternative for the whole scene (the animation itself is hidden from screen readers, which get this sentence): `Illustration: an agent in one window sends a question. Pigeon carries it to an agent in a second window, which wakes, reads it and sends an answer back. Nobody types in between.`

#### L2. How it works

**Job:** a newcomer reads four short panels and can explain Pigeon to someone else. No term that needs a definition, except "agent" and "topic", each explained once.

**Layout:** four panels in a row on desktop, stacked on a phone, numbered 1 to 4. Each has a small still illustration made from the pieces of the hook animation, a title, and one or two sentences. Large type, generous spacing. This layer should feel lighter than the rest of the site.

**Copy:**

- Section id: `how`
- Headline: `How it works`

| # | Title | Text | Illustration |
|---|---|---|---|
| 1 | `You work with two AI agents` | `An AI agent is an assistant that works on your code, such as Claude Code or Codex. Each one lives in its own window and cannot see the other.` | Two windows, no line between them. |
| 2 | `You introduce them once` | `Tell both agents to join the same conversation and give it a name, such as parser-review. Pigeon calls this a topic. It is private to those two.` | The line appears between the windows, with the name on it. |
| 3 | `They write to each other` | `Ask one agent to ask the other something. Pigeon carries the message across and wakes the other agent to read it and answer, as long as its chat is open.` | The violet note on the line, the second window lit. |
| 4 | `Nothing is lost while one is away` | `If the other agent is busy or its chat is closed, the message waits for it, for up to 24 hours.` | The note resting beside a dimmed window. |

- Line under the panels: `Pigeon runs on your own computer. You need no account, and your messages do not pass through anyone's servers.`
- Alt text, one per illustration: `Two separate agent windows.` / `The two windows joined by a line named parser-review.` / `A message travelling along the line to the second window, which lights up.` / `A message waiting beside a dimmed window.`

#### L3. Get started

**Job:** from "I want this" to two connected agents, with one line per item and a link to the detail. The beta line sits beside the download links, where the decision is made.

**Layout:** three numbered steps, each a block. Step 1 is three rows, one per operating system. Step 2 is five cards. Step 3 is two short command blocks.

**Copy:**

- Section id: `get-started`
- Headline: `Get started`
- Intro: `Three steps. You need Windows, macOS or Linux, and two of the tools in step 2.`

**Step 1**

- Title: `1. Install Pigeon`

| System | Download | One line | Detail link |
|---|---|---|---|
| `Windows` | Button `Download for Windows` to `[ASSETS]Pigeon-0.7.0-beta.2-win-x64.msi` | `Run the file. If Windows says "Windows protected your PC", choose More info, then Run anyway.` | `Windows steps` (`/Pigeon/install/#windows`) |
| `macOS` | Buttons `Apple Silicon` to `[ASSETS]Pigeon-0.7.0-beta.2-osx-arm64.pkg` and `Intel` to `[ASSETS]Pigeon-0.7.0-beta.2-osx-x64.pkg` | `Open the file. If macOS blocks it, allow it under System Settings > Privacy & Security > Open Anyway.` | `macOS steps` (`/Pigeon/install/#macos`) |
| `Linux` | Links `.deb` to `[ASSETS]pigeon_0.7.0.beta.2_amd64.deb`, `.rpm` to `[ASSETS]pigeon-0.7.0-0.beta.2.x86_64.rpm`, `.tar.gz` to `[ASSETS]pigeon-0.7.0-beta.2-linux-x64.tar.gz` | `Install the package with apt or dnf, or unpack the archive into a folder on your PATH. ARM64 files are on the Linux page.` | `Linux steps` (`/Pigeon/install/#linux`) |

- Beta line, directly under the three rows, in normal text and neutral color: `Pigeon is a public beta. Its installers are not signed yet, which is why Windows and macOS ask you to confirm. For now, try it with test conversations on a computer that only you use.` Link: `What the beta does and does not do` (`#beta`)
- Text: `Close your agent apps before you install and open them again afterwards. Then open a terminal and start Pigeon:`

```shell
pigeon start
```

- Small line: `Already installed? Run pigeon update --allow-pre-release to get the newest beta.`

**Step 2**

- Title: `2. Add Pigeon to your tools`
- Intro: `Pigeon reaches each tool through a small add-on called a plugin. Add it to the two tools you want to connect, then reopen them.`

Five cards. Each has the tool name, one line, and a link.

| Card title | One line | Link |
|---|---|---|
| `Claude` (small text: `Claude Code and Claude Desktop`) | `In Claude Code, run /plugin install pigeon --marketplace AndreVianna/Pigeon. On Windows, Claude Code also needs Git for Windows.` | `Claude steps` (`/Pigeon/install/#claude`) |
| `Codex` (small text: `Codex and the ChatGPT desktop app`) | `Run codex plugin marketplace add AndreVianna/Pigeon, then install pigeon and approve its hooks.` | `Codex steps` (`/Pigeon/install/#codex`) |
| `Antigravity` (small text: `Antigravity App 2.28 or later`) | `From a copy of the Pigeon repository, run agy plugin install ./integrations/antigravity-plugin, then turn on Pigeon wake.` | `Antigravity steps` (`/Pigeon/install/#antigravity`) |
| `OpenCode` (small text: `OpenCode 2, CLI or Desktop`) | `Copy the plugin folder from the Pigeon repository into OpenCode's plugins folder, then restart OpenCode.` | `OpenCode steps` (`/Pigeon/install/#opencode`) |
| `GitHub Copilot` (small text: `The app, the CLI, and Copilot chats in VS Code`) | `From a copy of the Pigeon repository, run copilot plugin install ./integrations/copilot-plugin` | `Copilot steps` (`/Pigeon/install/#copilot`) |

- Note under the cards, small: `Cursor and other tools are not supported yet.` Link: `Ask for a tool` (`[FEATURE]`)

**Step 3**

- Title: `3. Connect two agents`
- Text: `In each of the two agents, type the same thing:`

```text
Connect to Pigeon topic parser-review.
```

- Small line: `In Claude you can also type /pigeon:connect parser-review.`
- Text: `Then ask one of them to talk to the other:`

```text
Ask Codex to review the parser changes.
```

- Text after: `From then on they answer each other while both chats are open, without you typing.`

#### T1. Help test

**Job:** the beta's actual goal, placed where a new user has finished setup. Short.

**Copy:**

- Section id: `help-test`
- Headline: `Tell us what happened, especially on Mac and Linux`
- Body: `Pigeon has been tried with agents on Windows only. The beta guide has short tests for one machine and for two. Run the ones your setup allows and report each as pass, fail or not run. A failed install is a useful report.`
- Primary button: `Report your results` (`[REPORT]`)
- Secondary button: `Open the beta guide` (`[BETA_GUIDE]`)
- Text link: `Star Pigeon on GitHub` (`[REPO]`)
- Small line: `Reports are public GitHub issues. Use made-up messages and remove machine names, addresses, join codes and session IDs.`

#### L4. For engineers

**Job:** the reference an engineer scrolls down to find. Dense and scannable: tables, short lists, code. Nothing here is needed to start using Pigeon.

**Layout:** a clear break from layer 3 (a ruled line and the section title), then twelve blocks. A side index that stays in view on desktop lists the twelve anchors; on a phone it is a list at the top of the layer. Tables may scroll sideways on a phone or stack. Smaller type than layers 1 to 3 is fine.

**Copy:**

- Section id: `engineers`
- Eyebrow: `For engineers`
- Headline: `How Pigeon works, and where it stops`
- Intro: `Reference for the rest of the page: what Pigeon does, how it does it, its limits, and what this beta has and has not been tried with.`
- Index labels: `Capabilities`, `Architecture`, `Wake`, `Tools`, `Other machines`, `Limits`, `Beta status`, `Security`, `Settings`, `Commands`, `Questions`, `Documents`

##### E1. What Pigeon does

- Section id: `capabilities`
- Headline: `Capabilities`

Group `Messaging`

- `Topics: private conversations between exactly two sessions. The first to join creates the topic, the second completes it, a third is refused with a message naming both members.`
- `One session can be in several topics.`
- `Messages wait in order for the other member for up to 24 hours, including while that member is busy, away, or has not joined yet.`
- `A member that leaves hands its unread messages to whoever takes its place.`
- `Sending reports whether the recipient is connected, away, or absent from the topic.`
- `Stored, received and acknowledged are recorded separately. A message leaves the queue when the agent finishes the wake that received it, or when it is read with pigeon receive.`
- `A topic accepts 60 messages an hour by default, which stops two agents from answering each other without end.`

Group `Waking and recovery`

- `An idle, open conversation is woken when a message arrives, through the mechanism its tool supports.`
- `A burst of messages produces one wake. The agent receives up to 20 messages per call, from all its topics.`
- `A wake that starts no turn is retried, 3 attempts and 120 seconds apart by default.`
- `Messages survive stopping and starting the service.`
- `When a conversation is reopened or its app restarts, the plugin restores its topics. How far this goes depends on the tool.`

Group `Other machines (optional)`

- `A topic can be opened to another machine on the same trusted private network.`
- `Machines talk over mutual TLS and pin each other's certificate after the first join.`
- `The first join needs a 6-digit code passed between two people. Later joins need Allow or Deny.`
- `Approval prompts go to people, in the agent app, in a small Pigeon window, or in a terminal. Agents see only the outcome.`
- `Messages to another machine wait in an outbox and are retried until acknowledged or expired.`
- `A local topic can also require a join code (opt-in).`

Group `Running it`

- `Installers for Windows x64, macOS (Apple Silicon and Intel) and Linux (x64 and ARM64: .deb, .rpm, .tar.gz). No .NET needed, no administrator rights on Windows.`
- `pigeon update installs the newest release after checking its SHA-256.`
- `A command line for people: join, send and receive by hand, list topics, see what is waiting.`
- `Plugins for Claude Code and Claude Desktop, Codex and the ChatGPT desktop app, the Antigravity App, OpenCode 2, and GitHub Copilot.`
- `Optional erasing of message text after a number of days.`
- `No account, no cloud service, MIT License.`

##### E2. How it is built

- Section id: `architecture`
- Headline: `Architecture`

- `One background service per user, started with pigeon start. It listens on 127.0.0.1:29372 and keeps everything in one SQLite database.`
- `Each agent tool loads a Pigeon plugin. The plugin runs Pigeon's MCP server and adds the tool's hooks and a connect skill. Through the MCP server the agent gets tools such as pigeon_join, pigeon_send, pigeon_receive, pigeon_wake_complete, pigeon_topics and pigeon_leave.`
- `A session is the ID its tool gave the conversation (a Claude session ID, a Codex thread ID). Pigeon routes by it. It is a label, not a login.`
- `A topic has two seats. A message goes to the other seat and waits in order. If that seat is empty, the message is held for whoever joins next.`
- `There is no cloud backend and no task orchestration.`

**Visual:** an architecture diagram.

```text
agent A ── plugin (MCP server, hooks) ──┐                         ┌── plugin (MCP server, hooks) ── agent B
                                        │  Pigeon service          │
                                        └─ 127.0.0.1:29372 ────────┘
                                           SQLite message store
```

- Alt text: `Diagram: two agents, each connected by a Pigeon plugin (an MCP server and hooks) to one Pigeon service on 127.0.0.1 port 29372, which keeps messages in a SQLite store.`
- Link: `Architecture, in full` (`[ARCHITECTURE]`)

##### E3. How a wake works

- Section id: `wake`
- Headline: `How a wake works`

1. `A message is stored for the recipient. All messages waiting for one session share one wake.`
2. `The recipient's notifier starts a turn in its open conversation with a fixed instruction. The instruction names the wake, never the message text.`
3. `The agent calls pigeon_receive, which hands it up to 20 waiting messages from all its topics, each tagged with its topic and sender.`
4. `The agent handles them, replies with pigeon_send where that makes sense, and calls pigeon_wake_complete, which acknowledges what it received.`
5. `If the agent stops before step 4, the messages go back to the queue for a later wake. That is why delivery is at least once.`

- `A late or repeated wake instruction cannot receive a newer batch.`
- `A new message or a returning session re-arms a wake that gave up.`
- `A conversation that is closed has no process to wake. Its messages wait.`

**Visual:** a sequence diagram with four lanes: `Agent A`, `Pigeon service`, `Notifier for B`, `Agent B`. Arrows: send, store, wake instruction, receive, reply, complete. The violet accent marks the message path.

- Alt text: `Sequence diagram: agent A sends a message, the Pigeon service stores it, the notifier starts a turn in agent B, agent B receives the message, replies and completes the wake.`

##### E4. Supported tools

- Section id: `tools`
- Headline: `Supported tools`
- Intro: `The two sessions can be in the same tool or in different ones.`

| Tool | How an idle conversation is woken | Tried so far |
|---|---|---|
| `Claude Code and Claude Desktop` | `The plugin's hook wakes the open conversation. On Windows it runs under Git Bash, so Claude Code needs Git for Windows.` | `Windows` |
| `Codex and the ChatGPT desktop app` | `The plugin puts a wake prompt in the open chat.` | `Windows` |
| `Antigravity App 2.28 or later` | `Pigeon wake, which you turn on once in Antigravity's Automations dashboard, after the conversation has been idle for two seconds. The Antigravity CLI and IDE can send and receive but are not woken.` | `Windows, with a development copy of the plugin` |
| `OpenCode 2, CLI or Desktop` | `The plugin queues a prompt through OpenCode's background server; it runs after any turn in progress. OpenCode 1.x is not supported.` | `Windows, OpenCode 2.2.5, with a development copy of the plugin` |
| `GitHub Copilot: the app, the CLI, and Copilot chats in VS Code` | `A Copilot extension queues a prompt after the session has been idle for two seconds. VS Code's older Local chats are not woken.` | `Windows, in VS Code and the Copilot app, with a development copy of the plugin. Copilot CLI not yet tried.` |

- Note: `On macOS and Linux, Pigeon passes its automated tests, and the Apple Silicon and Ubuntu x64 installers are installed and checked on every release. No agent tool has been tried there yet. That is the main question of this beta.`
- Note: `Cursor and other tools are not supported yet.`

##### E5. Other machines

- Section id: `machines`
- Eyebrow: `Optional, off by default`
- Headline: `Two machines on a network you trust`
- Body: `Agents on two machines on the same trusted private network, such as a home or office LAN, can share a topic, including a colleague's machine. The two Pigeon services talk directly. Nothing is reachable from another machine until a person runs pigeon peer listen, an agent opens a topic to other machines at its person's request, and a person approves the join.`

Title `Steps`

1. `On each machine, once, a person runs pigeon peer listen. It prints the addresses other machines can use (port 29374).`
2. `Ask your agent to open a topic to other machines.`
3. `On the other machine, ask the agent there to join it by address the first time, for example review@192.168.2.20, and by machine name after that.`
4. `Pigeon shows the person who asked a 6-digit join code. They pass it to the person at the other machine, in person or by a call or chat, who types it in. Later joins from that machine need only Allow or Deny.`

Title `How a connection is made`

- `Each Pigeon service has its own key and certificate, created on first use. The private key never leaves your machine.`
- `The two services talk over TLS and each must present its certificate. After the first join, each side accepts only the certificate it pinned.`
- `Three wrong codes or ten minutes without an answer refuse the join.`
- `Agents see only the outcome. The code never appears in anything an agent can read through Pigeon.`
- `Pigeon does not change your firewall or router, and there is no relay on the internet.`

Title `Where the prompts appear`

- `Claude Code, Codex, Copilot CLI and OpenCode show them in their own interface.`
- `The Claude desktop app and Antigravity cannot, so Pigeon shows them in a small window of its own (Windows, macOS, and Linux with zenity or kdialog).`
- `With nobody at the session, or in a headless run, use the terminal: pigeon requests, pigeon allow, pigeon deny.`

Title `What another machine can and cannot do`

- `A known machine can see the topics you opened to other machines, ask to join one, exchange messages in the topics it joined, and leave.`
- `It cannot join without a person's answer, reach a topic you did not open, see your session IDs, read your status, or stop your service.`
- `Either person can end a topic or forget the other machine at any time.`

Title `Limits we accept in this beta`

- `Another account on your computer can use your Pigeon service, including its topics with other machines. Use this on single-user machines.`
- `On the first contact only the machine that opened the topic checks the code. An active attacker already on your network who takes that machine's address could pose as it.`
- `An agent that can run shell commands could run Pigeon's approval commands, and an agent that can control your screen could click an approval window. Pigeon does not try to prevent that.`
- `When one of the machines is a Mac, the link uses TLS 1.2, and someone on the network can tell which two machines are talking, though not what they say.`
- `Both machines need to accept an incoming connection on port 29374. Managed corporate devices and guest Wi-Fi often block that.`
- `There is no relay and no discovery. The first join names the other machine's address.`

- Status line, body text and not a footnote: `Tried so far between two Windows machines on a home Wi-Fi network. Pigeon also listens on Tailscale addresses, but Tailscale, macOS and Linux have not been tried between machines yet.`
- Link: `Remote connections design, with threats and mitigations` (`[REMOTE_DESIGN]`)

**Visual 1:** the link diagram.

```text
machine A                                          machine B
agent ── service A ════ mutual TLS ════ service B ── agent
```

- Alt text: `Diagram: an agent and its Pigeon service on machine A, connected by a mutual TLS link to the Pigeon service and agent on machine B.`

**Visual 2:** a screenshot of Pigeon's join-code window, to be captured by the owner on Windows.

- Caption: `The join code appears in the agent app or in a small Pigeon window, outside the conversation.`
- Alt text: `A small Pigeon window showing a 6-digit join code and the instruction to give it to the person at the other machine.`

**Snippet** (format from the CLI source; confirm at capture):

```console
$ pigeon peer listen
Other machines can reach "dev-laptop" at 192.168.2.20:29374. Allow that port in the firewall if your system asks; Pigeon does not change it.
Someone on another machine joins one of your topics as <topic>@192.168.2.20 the first time.
```

##### E6. Limits, with numbers

- Section id: `limits`
- Headline: `Limits`

| What | Value |
|---|---|
| `Members of a topic` | `2. A third is refused.` |
| `How long a message is held` | `Up to 24 hours.` |
| `Message length` | `Up to 16,384 characters.` |
| `Messages per topic` | `60 an hour by default.` |
| `Delivery` | `At least once. The same message can occasionally arrive twice.` |
| `Wake` | `Only while the conversation is open and its tool supports it.` |
| `Wake retries` | `3 attempts, 120 seconds apart, by default.` |
| `Topic lifetime` | `Deleted when its last member leaves, or after both members have been disconnected for 60 minutes of service running time, with any messages still waiting.` |
| `The service` | `Does not start with the computer. Run pigeon start after a restart or log-out.` |
| `Join code` | `6 digits, 3 attempts, 10 minutes.` |
| `Caller authentication on your computer` | `None. See Security.` |

Title `Known issues worth knowing first`

- `Claude: to stay reachable, an idle conversation gets a short renewal turn every 12 hours, at most two a day. It is not a message.`
- `Claude Desktop: after you reopen a conversation or restart the app, the conversation may not start its process until you type in it, and it cannot be woken until then.`
- `Codex: a chat whose Pigeon connection ended, for example because the service was stopped for an update, is not reconnected by Codex. Reopen the chat or restart the app.`
- `Codex can show a wake prompt that was already handled. Pigeon rejects it.`
- `Antigravity: after the service restarts, a conversation is not woken until its next turn.`
- `Pigeon does not reserve files. Two agents can still edit the same file at once.`

- Link: `All known issues` (`[KNOWN_ISSUES]`)

##### E7. Where the beta stands

- Section id: `beta`
- Headline: `Where the beta stands`
- Intro: `Pigeon 0.7.0-beta.2 is an exploratory beta. This is what has been tried and what has not.`

Two columns of equal weight, neutral colors.

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

Title `Four things to know before you rely on it`

1. **`The installers are not signed yet.`** `Windows and macOS ask you to confirm them. Each release lists SHA-256 checksums, which detect a changed download but are not a signature.`
2. **`There are no passwords on your computer.`** `The Pigeon service does not check who is calling it. Use a computer that only you use, and do not send secrets through Pigeon.`
3. **`Other machines only on a trusted private network.`** `The network listener is off until you turn it on. Do not forward its port on a router.`
4. **`Use test conversations for now.`** `Try the beta with new conversations and made-up messages, not real work.`

- Buttons: `Report your results` (`[REPORT]`), `Open the beta guide` (`[BETA_GUIDE]`)

##### E8. Security model

- Section id: `security`
- Headline: `Security model`

Title `On your computer: no caller authentication`

- `The service listens on 127.0.0.1 only, so it is not reachable from the network. It has no authentication. A topic name and a session ID are labels that route messages; they do not prove who is sending.`
- `Any program running on the computer, under any user account, can join a topic, use any session ID, read and send messages, answer a join request from another machine, or stop the service.`
- `The service rejects requests that come from a web page in a browser. That reduces one kind of attack; it does not identify the caller.`
- `What to do: run Pigeon on a computer that only you use, do not send passwords or keys through it, and do not expose the local port (29372) to the network.`
- `Authenticating local callers is planned for a later version.`

Title `Between agents: messages are data`

- `Pigeon delivers a message as information from a peer, and its plugins instruct each agent to treat it that way. A message grants nothing: it does not answer your tool's permission prompts, change its sandbox, or authorize a shell command.`
- `This is a rule the agents are told to follow, not a filter. Pigeon does not inspect messages, and an agent that you already allow to run commands without asking can act on what a message says. Give two connected agents the permissions you would give either one alone.`

Title `Between machines`

- `See Other machines above for the connection, what another machine can do, and the accepted limits.` Link: `Other machines` (`#machines`)

Title `Installers and updates`

- `The Windows installer and the macOS packages are not signed, so Windows SmartScreen warns about an unknown publisher and macOS Gatekeeper blocks the package until you allow it.`
- `Every release includes SHA256SUMS.txt. A matching checksum shows that your download is the file that was published. It does not prove who published it.`
- `pigeon update downloads from this project's GitHub releases, refuses a file that does not match its line in SHA256SUMS.txt, and never installs an older version.`

Title `What is stored`

- `Messages are kept in a SQLite database in your user profile. The database is not encrypted.`
- `A topic shared with another machine keeps its messages on both machines.`
- `PIGEON_BODY_RETENTION_DAYS erases the text of read or expired messages after that many days. This is not secure erasure: database files and backups can still contain the text.`
- `Pigeon sends nothing to its author or to any service. It uses the network only for pigeon update (GitHub) and for the machines you approved.`

Owner: the last sentence rests on a search of the source for outbound addresses. Confirm before publishing.

Title `Report a security problem`

- `GitHub issues are public. For a problem that should not be public yet, open an issue that says only that you have a security report, without the details, and the maintainer will arrange a private channel.` Link: `Open an issue` (`[FEATURE]`)

If the owner turns on GitHub's private vulnerability reporting, replace that sentence with `Use GitHub's private report form. Only the maintainer sees it.` and link `Report privately` to `https://github.com/AndreVianna/Pigeon/security/advisories/new`.

- Link: `Local trust boundaries, in full` (`[LOCAL_TRUST]`)

##### E9. Settings

- Section id: `settings`
- Headline: `Settings`
- Intro: `Environment variables, set before pigeon start. The port must also be set for the agent tools.`

| Variable | Default | Meaning |
|---|---|---|
| `PIGEON_PORT` | `29372` | `The local port. The service, the plugins and the commands must all use the same one.` |
| `PIGEON_DATABASE` | `%LOCALAPPDATA%\Pigeon\messages.db` on Windows, `~/Library/Application Support/Pigeon/messages.db` on macOS, `~/.local/share/Pigeon/messages.db` on Linux | `Where messages are stored (an absolute path).` |
| `PIGEON_MAX_MESSAGES_PER_HOUR` | `60` | `Messages one topic accepts per hour.` |
| `PIGEON_ABANDONED_TOPIC_MINUTES` | `60` | `How long both members can be away before a topic is deleted.` |
| `PIGEON_BODY_RETENTION_DAYS` | `not set` | `When set, the text of read or expired messages is erased after this many days.` |
| `PIGEON_MAX_ATTEMPTS`, `PIGEON_MAX_WAKE_ATTEMPTS`, `PIGEON_WAKE_RETRY_SECONDS` | `3`, `3`, `120` | `How often an unread message and a wake are retried.` |
| `PIGEON_PROMPTS` | `not set` | `Set to terminal to answer join requests in a terminal and turn off Pigeon's own windows.` |

- Line under the table: `The port between machines is 29374. Change it with pigeon peer listen <address>:<port>.`

##### E10. Commands

- Section id: `commands`
- Headline: `Commands`
- Intro: `Words such as as, using, from, to and for are part of the commands. A session ID is the ID your agent tool gives its conversation; the plugins find it for you, so you need it only when you type the commands yourself.`

| Command | What it does |
|---|---|
| `pigeon start [--foreground]` | `Starts the service in the background. --foreground keeps it in the current terminal.` |
| `pigeon stop` | `Stops the service. Waiting messages are kept.` |
| `pigeon status` | `Shows whether the service runs, which sessions are connected, and what is waiting.` |
| `pigeon topics [on <machine>]` | `Lists topics, their members and waiting messages, or the topics a known machine has open.` |
| `pigeon join <topic> as <session-id> using <tool> [--remote] [--approval]` | `Joins a topic, creating it if it is new. --remote opens it to other machines. --approval makes a local joiner give a join code.` |
| `pigeon leave <topic> as <session-id>` | `Leaves a topic. The last one to leave deletes it.` |
| `pigeon send from <session-id> to <topic> "<message>"` | `Sends a message to the other member. Use - as the message to read it from standard input.` |
| `pigeon receive for <session-id> from <topic>` | `Shows the messages waiting for that session and removes them from the queue.` |
| `pigeon peer name [<name>]` | `Names this machine for others. The default is its host name.` |
| `pigeon peer listen [<address>[:<port>]]` | `Lets other machines reach this one, on its LAN and Tailscale addresses or on one address.` |
| `pigeon peer listen off` | `Stops accepting other machines.` |
| `pigeon peer list` | `Shows known machines and the topics with them.` |
| `pigeon peer remove <name>` | `Forgets a machine. Its topics end at once.` |
| `pigeon peer address <name> <address>[:<port>]` | `Updates where a machine is reached.` |
| `pigeon requests [--show <request>]` | `Shows join requests waiting for an answer. --show prints the join code of your own request.` |
| `pigeon allow <request> [<code>]` | `Allows a join request, or confirms contacting a new machine.` |
| `pigeon deny <request>` | `Refuses a join request.` |
| `pigeon update [--allow-pre-release]` | `Installs the newest release from GitHub. Stable versions only unless the flag is given; never an older version.` |
| `pigeon --help`, `pigeon <command> --help`, `pigeon --version` | `Help and the version.` |

- Line under the table: `Exit codes: 0 on success, including when a message is held for later; 1 when Pigeon refuses or cannot do something; 2 when the command is written wrongly.`
- Line: `pigeon mcp, pigeon hook and pigeon sidecar are used by the plugins. You do not run them yourself.`

**Visual:** the terminal transcript, verbatim from the README. This is the place for a real terminal block.

- Title above it: `The same thing your agents do, typed by hand`

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

##### E11. Questions

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

**`Is Cursor supported?`**
`Not yet. Today Pigeon has plugins for Claude, Codex, Antigravity, OpenCode 2 and GitHub Copilot.`

**`Does Pigeon use my model's tokens?`**
`Each wake is a turn in your agent, like a message you typed. In addition, an idle Claude conversation with a joined topic gets a short renewal turn every 12 hours so that it stays reachable; that turn is not a message and does nothing else.`

**`Do I need .NET?`**
`Not for the installers. They include what Pigeon needs. Building from source needs the .NET 11 SDK.`

**`What does it cost?`**
`Nothing. Pigeon is open source under the MIT License.`

##### E12. Documents

- Section id: `docs`
- Headline: `Documents`
- Links: `README` (`[README]`), `Beta testing guide` (`[BETA_GUIDE]`), `Known issues` (`[KNOWN_ISSUES]`), `Architecture` (`[ARCHITECTURE]`), `Remote connections design` (`[REMOTE_DESIGN]`), `Local trust boundaries` (`[LOCAL_TRUST]`), `Changelog` (`[CHANGELOG]`), `All files for 0.7.0-beta.2` (`[RELEASE]`)

#### F1. Footer

**Copy:**

- Line 1: `Pigeon is open source under the MIT License. Made by Andre Vianna.`
- Links: `GitHub` (`[REPO]`), `Releases` (`[RELEASE]`), `Beta guide` (`[BETA_GUIDE]`), `Known issues` (`[KNOWN_ISSUES]`), `Report a beta result` (`[REPORT]`), `Suggest a feature` (`[FEATURE]`)
- Line 2: `This site sets no cookies and runs no analytics.`
- Line 3: `Pigeon is an independent project. Claude, Codex, ChatGPT, Antigravity, OpenCode and GitHub Copilot are products of their respective owners.`

---

### Install

The copy of this page is the same as in version 1, repeated below so this file stands on its own. Two changes:

- **Anchors.** Each operating system panel and each tool panel gets an id, because Home links to them: `#windows`, `#macos`, `#linux`, `#claude`, `#codex`, `#antigravity`, `#opencode`, `#copilot`. Arriving at one of these anchors opens that tab and scrolls to it. With JavaScript off, the panels are stacked and the anchor scrolls to the right one.
- **Tab labels.** Use the short labels `Claude`, `Codex`, `Antigravity`, `OpenCode`, `Copilot`, as on the Home cards. The long names stay as the first line inside each panel.

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

| Action | Label | Destination | Where it appears |
|---|---|---|---|
| **Primary: download the beta** | `Download the beta` | `#get-started` on Home | Header on every page, hook (L1) |
| Primary, at the point of download | `Download for Windows`, `Apple Silicon`, `Intel`, `.deb`, `.rpm`, `.tar.gz` | Direct files under `[ASSETS]` | Get started, step 1 (L3); Install page, step 1 |
| Detail links | `Windows steps`, `Claude steps` and the like | Install page anchors | Get started, steps 1 and 2 (L3) |
| **Secondary: report results** | `Report your results` | `[REPORT]` | Help test (T1) as its main button; beta status (E7); Install closing section; footer |
| **Secondary: beta guide** | `Open the beta guide` | `[BETA_GUIDE]` | Help test (T1); beta status (E7); documents (E12); Install closing section; footer |
| **Tertiary: star the repository** | `Star Pigeon on GitHub` | `[REPO]` | Help test (T1), as a text link. No star count while it is near zero. |
| Tertiary: source | `GitHub` | `[REPO]` | Header, footer |
| Tertiary: ask for a tool or feature | `Ask for a tool`, `Suggest a feature` | `[FEATURE]` | Under the tool cards (L3), footer |

Rules:

- The first screen has one button. "See how it works" is a text link.
- The header and hook buttons lead to "Get started", not to a file, so every download passes the beta line.
- The report button is the main button only in Help test and in the beta status block.

---

## 6. Trust and honesty

The four statements are the same as in version 1. Their placement follows the owner's direction: nothing in the hook, one short line at the download, the full account in layer 4.

| What must be said | Short form, and where | Full form |
|---|---|---|
| **It is a beta, tried with agents on Windows only.** | The button label `Download the beta` (L1). The beta line under the download links (L3). The first sentence of Help test (T1). | E7, and the "Tried so far" column in E4. |
| **The installers are not signed.** | The beta line (L3), and the one-line instruction in the Windows and macOS rows, which tells the reader what to press. | E7 point 1, E8 "Installers and updates", and the Install page at the step where the warning appears. |
| **The local service does not authenticate callers.** | The beta line (L3): "on a computer that only you use". | E8 "On your computer", E7 point 2, and the "Before you start" box on the Install page. |
| **Network use only on trusted private networks.** | Not mentioned in layers 1 to 3, because the network feature is not offered there. | E5, whose headline and body carry the condition, and E7 point 3. |

Rules that still hold:

- The animation shows a wake. The panel that explains it (L2, panel 3) says "as long as its chat is open", and panel 4 says "for up to 24 hours".
- "Tried" and "Not yet tried" are two columns of equal weight. No warning colors, no alarm icons.
- Do not claim: exactly-once delivery; a wake after every reopen; that Pigeon prevents an agent from acting on a message; that Pigeon has been tried on macOS, Linux or Tailscale; that the network link hides which machines are talking when a Mac is involved; that another local account is kept out; support for Cursor or any tool outside the five listed.

---

## 7. Success measures and open questions

### Success measures

Unchanged from version 1: no analytics on the site; read release download counts, issues labeled `beta report`, the site as a referrer in the repository's traffic, and stars, with the same commands and baseline (1 download per file, 0 issues, 0 stars on 2026-11-24).

One addition that version 2 makes possible without analytics: because the Home download links are direct files, the per-file download counts now show which system the site's visitors chose.

### Open questions for the owner

New in version 2:

1. **Cursor.** The request named Cursor among the tools. Pigeon has no Cursor plugin today; the repository holds only research and a probe, and that research found no documented way to wake an idle Cursor chat. The copy says "Cursor and other tools are not supported yet" under the tool cards, in the tools block and in the questions. Keep the note, or leave Cursor unnamed?
2. **Headline.** "Let your AI agents talk to each other." replaces "Stop being the messenger between your AI agents." Keep the old line as the social-post line, or prefer it here?
3. **The animation.** It is specified as an illustration built with the site's own shapes. Who builds it, and is a still frame acceptable for the first release of the site if the animation is not ready?
4. **Tool names in the animation.** The two windows are labeled "Claude Code" and "Codex" in text. Confirm, or use neutral labels such as "Agent A" and "Agent B".
5. **Security page.** Version 2 folds it into the home page. If a separate address for security is wanted (for listings or reviewers), the E8 and E5 blocks can also be published as their own page.
6. **The README's command table is out of date.** It lists `pigeon peer invite|accept|confirm` and `pigeon topic share|unshare`, which the changelog says were removed in 0.6.0-alpha.4, and it omits `pigeon requests`, `pigeon allow` and `pigeon deny`. The commands block here follows the CLI's own help. The README needs the same correction.
7. **Home is long.** Layer 4 carries what was a separate page. If it proves too long on a phone, the blocks E9 (settings) and E10 (commands) are the first candidates to move to their own page.

Still open from version 1 (see its section 7 for detail): where the site lives in the repository and how it is deployed; who updates version strings on a release; documentation links pinned to the tag or to `main`; real 0.7.0-beta.2 command output and the join-code window capture; which copy of the repository testers clone for the Antigravity, OpenCode and Copilot plugins; the changelog still marking beta.2 "unreleased"; the "no telemetry" statement; a private security contact; the independence line in the footer; self-hosted fonts; a custom domain.

The recording of two real sessions (version 1, question 4) is no longer needed for the first screen. It would still strengthen listings and could sit at the top of layer 4.
