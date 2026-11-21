# Pigeon 0.7.0-beta.1 announcement kit

Drafts for the owner to review and reuse. The [prerelease](https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1) is public. The owner reports publishing the LinkedIn, r/ClaudeCode and Facebook posts and sending the Console.dev pitch; X was skipped because account verification email was unavailable. Publication links have not been independently checked. Send testers to the [beta guide](https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md), the [beta report form](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml), and [New issue](https://github.com/AndreVianna/Pigeon/issues/new/choose) for feature requests. The [researched channel plan](beta-launch-channels.md) ranks more venues and records their submission rules and prerequisites.

## Distribution plan

| Venue | Treatment |
|---|---|
| LinkedIn | A personal product announcement with **no links in the post**, then a **first comment** with the release, test guide, and feedback links. The owner reports publishing it. |
| Reddit r/ClaudeCode | A candid "try to break it" challenge in the [weekly showcase thread](https://www.reddit.com/r/ClaudeCode/comments/1xrpcjt/weekly_showcase_thread_what_are_you_building_with/). The owner reports posting it. |
| Reddit r/ClaudeAI | Optional second audience via its [project showcase hub](https://www.reddit.com/r/ClaudeAI/comments/1xq4qd2/claude_project_showcase_discussion_hub_updated_on/). Its [showcase rules](https://www.reddit.com/r/ClaudeAI/comments/1qe5wtt/rule_7_is_getting_a_glowup_less_spam_more_how_the/) require a truthful account of how Claude helped build or test the project; standalone feed posts have a karma threshold. Adapt the Reddit copy instead of cross-posting it unchanged. |
| Console.dev | Strong editorial beta listing for developer tools. [Its criteria](https://console.dev/selection-criteria) fit Pigeon's pre-1.0 release. The owner reports sending the pitch, including the Mac/Linux gaps. |
| Developer tool listings | [DevHunt](https://devhunt.org/faq) accepts developer CLIs and offers a free permanent listing; a featured launch week costs $49. Describe Pigeon as a CLI messaging app with agent integrations. |
| AlternativeTo | An [email-verified, reviewed](https://alternativeto.net/faq/) evergreen software listing. Prepare platform, MIT license, and accurate alternatives/category after the first beta reports. |
| Agent plugin directories | [Claude's directory](https://claude.com/resources/articles/build-plugins-for-claude) has a reviewed GitHub plugin-bundle route. [Awesome Copilot](https://github.com/github/awesome-copilot/blob/main/CONTRIBUTING.md#adding-external-plugins) has a reviewed path for external GitHub-hosted plugins. These are integration-specific follow-ups, not current listings. |
| GitHub release | The already published release is the download destination. The full announcement below could replace its short notes after review. GitHub Discussions is currently disabled for this repository. |
| X | A compact developer-facing announcement, perhaps followed by a reply with the beta challenge. Its [normal posts have a 280-character limit](https://help.x.com/en/using-x/types-of-posts); links are shortened by X. Prioritize it if the owner's account already reaches developers. Community posts must also follow that community's [own rules](https://help.x.com/en/using-x/communities-moderator-playbook). |
| Facebook | The owner reports posting the personal beta request. For any group post, check its rules first: [Meta says group admins set rules and may decline posts](https://about.fb.com/news/2019/08/private-groups-safety/). |
| Bluesky or Mastodon | Optional short pointer to the release and the Mac/Linux testing request if the owner has a developer audience there. Reuse the X substance, with a native tone. |
| DEV Community | Later, a technical article on durable queues, wake confirmation, and the difference between stored and acknowledged messages. [DEV asks authors to disclose AI assistance](https://dev.to/code-of-conduct). |
| Product Hunt | Defer until there is a concise demo and screenshots. Its [launch requirements include a logo and gallery image](https://help.producthunt.com/en/articles/17734646-8-launch-faq-and-myths). It can be a separate, planned launch rather than another copy of this beta request. |
| Uneed | [Uneed](https://help.uneed.best/getting-started/how-to-submit-your-first-product) has a moderated free queue that may take months. Prepare visual assets first. |
| Hacker News | Optional Show HN when the owner can discuss the implementation. [Show HN accepts runnable early-stage projects](https://news.ycombinator.com/showhn.html), but [HN prohibits generated or AI-edited submission and comment text](https://news.ycombinator.com/newsguidelines.html). The owner must write that post personally; use the factual checklist below, not copy from this kit. |
| r/dotnet and r/MacApps | Targeted platform communities, with stricter rules. [r/dotnet](https://www.reddit.com/r/dotnet/comments/1rgx9j4/rule_change/) limits promotion to its weekend window and disallows AI-written posts; [r/MacApps](https://www.reddit.com/r/macapps/comments/1xo5ky1/rmacapps_moderation_update_phase_4_clearer_rules/) has account and frequency limits. Use only after checking current rules. |

Start with LinkedIn, r/ClaudeCode, and an editorial pitch to Console.dev, then consider developer-tool listings such as DevHunt. Add X when account access is available; add Facebook only through a personal network or a group that allows it. Share elsewhere when the owner can answer questions. Avoid identical cross-posts.

The [solo publishing plan](beta-launch-plan.md) fits the initial outreach into one two-hour session; replies and beta reports are handled afterward. The LinkedIn post can use [this infographic](media/pigeon-linkedin-infographic-v5.png). Claude Code and Codex are a concrete example of two different agent tools; the post names the other supported tools. The pigeon appears as the symbol inside the routing hub; it does not carry messages itself. Suggested alt text: “Before: a person carries a message between Claude Code and Codex. With Pigeon: the two tools exchange messages through a hub, on the same machine or within a trusted private network.”

## Console.dev: editorial beta pitch

Copy of the pitch the owner reports sending; the sent message has not been independently inspected.

**Subject:** Pigeon beta: AI coding sessions can talk to each other

Hi Console team,

I kept finding myself in the middle of two AI coding sessions working on the same repo: copying a question from Claude Code into Codex, then carrying the answer back. I built Pigeon so the sessions could exchange those messages without me.

Two existing sessions join a topic. If one is busy, its messages wait. Where the tool supports it, Pigeon can wake that same conversation, and I can tell whether a message was merely queued or actually acknowledged. The beta has integrations for Claude Code, Codex, Antigravity, OpenCode 2 and GitHub Copilot. It also supports two machines on a trusted private network after an explicit join and approval.

Pigeon is free, MIT-licensed and open source. The first public beta is available for Windows, macOS and Linux: https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1

I've tested agent workflows on Windows. The Apple Silicon and Ubuntu x64 installers passed automated checks, but I still need people to try real agent wake and recovery on macOS and Linux. The short test guide is here: https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md

Would Pigeon be a fit for Console's beta listings? I'd welcome your feedback either way.

Thanks,
Andre Vianna

## DevHunt: developer CLI listing

DevHunt's free listing is a product page, separate from its $49 featured launch week. Its submission asks for a name, website, description, logo and screenshots. Use the public GitHub repository as the product URL and describe the CLI first; MCP is how the agent plugins connect to it.

- **Name:** Pigeon
- **Website:** https://github.com/AndreVianna/Pigeon
- **Short description:** A local CLI that lets existing AI coding sessions exchange durable messages, with optional two-machine conversations on a trusted private network.
- **Category:** Developer tools or CLI tools, depending on the choices shown in the form.
- **Long description:** Pigeon is a free, open-source CLI and local messaging service for two existing AI coding sessions working on the same project. Its plugins connect through MCP. The sessions join a topic, exchange messages, and can catch up after a busy period or restart. Where the host supports it, Pigeon wakes the existing conversation. It records queued messages separately from agent acknowledgments. Integrations cover Claude Code/Desktop, Codex/ChatGPT desktop, Antigravity App, OpenCode 2 and GitHub Copilot. Two machines can share a topic on a trusted private network after an explicit join and approval. This is an exploratory beta: Windows agent workflows have been tested; macOS and Linux wake and recovery need testers.
- **Beta note:** Integrations for Claude Code, Codex, Antigravity, OpenCode 2 and GitHub Copilot. Windows agent workflows tested; macOS and Linux agent wake and recovery need testers.
- **Logo:** [square transparent Pigeon mark](media/pigeon-devhunt-logo.png), prepared for review.
- **Gallery image:** [Pigeon before/after infographic](media/pigeon-linkedin-infographic-v5.png). Label this as an illustration of the workflow, not a screenshot of the CLI.

The repo's GitHub **About** box currently has no description or topics. Before listing, add this description: “A CLI for durable messaging between existing AI coding sessions, locally or across a trusted private network.” Suggested topics: `ai-agents`, `coding-agents`, `cli`, `dotnet`, `claude-code`, `codex`. A real CLI screenshot would make the listing stronger; do not present the infographic as one.

## Full release announcement

### Pigeon 0.7.0-beta.1: let two AI coding sessions exchange messages

I built Pigeon because I wanted Claude Code and Codex working on the same repository to exchange messages without my copying text between their windows. Pigeon is a free, MIT-licensed CLI and plugin system. Agents connect through MCP to one shared local service and message store. A topic holds a conversation between two sessions; each can send, read, and reply there.

Current capabilities:

- **Durable messaging.** The local SQLite-backed service queues messages in order when the other session is busy or disconnected. It records delivery attempts and acknowledgments, retries wakes, and keeps a queued or notified message distinct from one the agent actually processed.
- **Agent plugins.** Bundled integrations cover Claude Code and Claude Desktop, Codex and the ChatGPT desktop app, Antigravity App, OpenCode 2, and GitHub Copilot. They add MCP tools and, where the host supports it, notification of an existing conversation. Pigeon does not start a replacement agent. Wake and session recovery vary by host.
- **CLI control.** `pigeon` starts and stops the service, lists topics and connection status, and lets a person join, leave, send, and receive messages. Plugin connect workflows handle session IDs for agents.
- **Optional two-machine conversations.** On a trusted private network, two services can exchange messages over TLS with pinned machine identities. First contact uses a join code and a person's approval, and a topic is shared explicitly. The peer listener is off by default.
- **Cross-platform packaging.** Self-contained Windows x64 MSI, Apple Silicon and Intel Mac PKG, and Linux DEB, RPM, and tar archives for x64 and ARM64. The release includes SHA-256 hashes and does not require a separate .NET runtime for these packages.

This is an **exploratory beta**. Agent messaging has been exercised on Windows, including Claude and Codex. Two Windows machines were also tried with Claude Desktop and Antigravity. The release workflow installed and smoke-tested the Windows x64, Apple Silicon, and Ubuntu x64 packages. Intel Mac and Linux ARM64/RPM packages were built but did not get equivalent native install checks. **Real Claude and Codex session wake and recovery on macOS and Linux are not yet verified.** That is the main reason for this invitation.

The Windows and macOS installers are unsigned. The local service does not authenticate callers, so use disposable sessions and synthetic messages on a single-user machine. Remote sharing is optional and belongs only on a trusted private network. Peer-agent messages do not grant permissions in either agent app. See [known issues](https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/known-issues.md).

Try [Pigeon 0.7.0-beta.1](https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1) with the [beta guide](https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md). Please [report bugs and partial results](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml), especially whether a message was stored, found by polling, woke an idle session, and was acknowledged. [Feature requests](https://github.com/AndreVianna/Pigeon/issues/new/choose) are welcome too. Mac and Linux results are especially valuable, including failures at the install step.

## LinkedIn: announcement post

Sometimes I have multiple tools, like Claude Code and Codex, working on the same repo. Different models, different specialties, or just a way to split the token usage.

But it was a pain. Claude had a question, so I copied it into Codex. Codex answered, so I copied that back. After enough trips between windows, I said to myself: I can do better.

So I built Pigeon.

Pigeon lets two existing agent sessions, even in completely different tools, join a topic and exchange messages. If one is busy, the message waits. If its tool supports notification, Pigeon can wake that same session with its conversation context intact.

I can also see when a message was queued and when the other agent acknowledged it.

Pigeon is public, free, and open source.

The current beta has integrations for Claude Code, Codex, Antigravity, OpenCode 2, and GitHub Copilot. The sessions can be on the same machine or on two machines within a trusted private network, with an explicit join and approval for the connection.

I've tested agent workflows on Windows. I'd love help trying wake and recovery on Mac and Linux.

Would you like to help test it?

Download and test guide in the first comment.

## LinkedIn: first comment requesting beta help

If you try Pigeon, pick two fresh chats in tools you already use. Ask one to send the other a made-up question, then leave the receiving chat alone. Did it answer on its own? Did you have to ask it to check? Tell me which two apps you used and what happened.

The Apple Silicon and Ubuntu x64 installers passed automated checks. I still need someone to try waking and reopening real agent conversations on macOS and Linux. A failed install is worth reporting too. If you try two machines, use a trusted private network; the guide explains the join and approval steps.

The installers are unsigned and Pigeon does not authenticate local callers. Please use a machine only you use and stick to made-up messages.

Release: https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1

Test guide: https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md

Report what happened: https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml

Suggest a feature: https://github.com/AndreVianna/Pigeon/issues/new/choose

## Reddit: r/ClaudeCode test-of-fire challenge

For a standalone post, if the current subreddit rules permit one: **I got tired of carrying messages between Claude Code and Codex. Can you break what I built?** In the weekly showcase, use the text below as a comment without a title.

### Short comment for the weekly showcase

I got tired of being the messenger between Claude Code and Codex. Claude had a question; I pasted it into Codex. Codex replied; I carried the answer back. So I built Pigeon.

Two existing agent sessions join a topic and exchange messages through Pigeon. They can be in different tools: Claude Code/Desktop, Codex/ChatGPT desktop, Antigravity, OpenCode 2 or GitHub Copilot. They can also be on two machines on a trusted private network, after an explicit join and approval. Claude Code helped me test the part that wakes an idle conversation.

The first public beta is out, and I'd love for this community to put it through a test of fire. Send Claude a made-up question while it's busy. Does it pick up the message when it finishes? Leave it idle and try again. If nothing happens until you type “check your messages,” tell me that too. That's exactly the sort of failure I need to find.

I've tested agent workflows on Windows. Mac and Linux wake and recovery still need real-world testing. Please use fresh chats and fake messages on a single-user machine; the installers are unsigned and local callers aren't authenticated.

[Get the beta](https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1) · [Tell me where it broke](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml)

### Longer version for a substantive standalone post

I sometimes have Claude Code and Codex working on the same repo. Claude asks Codex a question, I copy it over, Codex answers, I copy that back. I was doing enough of this that I built **Pigeon** to get myself out of the middle.

Two existing agent sessions join a topic and exchange messages through a small local service. If one is busy, its messages wait. If its tool supports notification, Pigeon can wake that same conversation. I can see when a message was queued and when the other agent actually acknowledged it. It's free and open source, with integrations for Claude Code/Desktop, Codex/ChatGPT desktop, Antigravity, OpenCode 2 and GitHub Copilot. Two machines can share a topic on a trusted private network, with a join code and approval.

The first public beta, **0.7.0-beta.1**, is out. I'd love some help trying to break it:

- Send a few fake messages while the other agent is busy. What happens when it finishes?
- Leave a session idle. Does it wake by itself, or only after you tell it to check its inbox?
- Queue a message, then restart Pigeon. Is the message still there?
- Close or archive a conversation and reopen it. Does it reconnect to the right topic?
- Try it on Mac or Linux. The installers passed automated checks, but real agent wake and recovery on those systems still need testing.

I've tested agent workflows on Windows. The installers are unsigned and local callers aren't authenticated, so please use fresh conversations and made-up messages on a single-user machine. Only use the two-machine feature on a trusted private network.

If it breaks, I'd really like to know *where*: did Pigeon store the message, did the agent find it when asked, did the session wake on its own, and did the agent acknowledge it? A partial failure or a feature idea is useful too.

[Get the beta](https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1) · [Test guide](https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md) · [Report what happened](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml)

For r/ClaudeAI, add one sentence in the owner's own words explaining a specific way Claude helped build or test Pigeon, then use its showcase hub if the account does not meet the standalone-post threshold.

## X: announcement and reply

Post 1 (attach the [infographic](media/pigeon-linkedin-infographic-v5.png) if useful): I got tired of copying questions between Claude Code and Codex, so I built Pigeon. It lets two existing AI coding sessions talk through a shared topic. Where the tool allows it, Pigeon can wake that same conversation. First public beta: https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1

Post 2 (reply to the first): It also works with Antigravity, OpenCode 2 and GitHub Copilot, including two machines on a trusted private network. Windows workflows are tested; I need Mac/Linux testers. Try to break wake and recovery, then tell me where it failed: https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml

## Facebook: personal network or an approved developer group

I've been using different AI coding tools on the same project, and I kept ending up as their messenger. Claude Code would have a question for Codex, so I'd copy it across, then carry the answer back. I finally built something to handle that part: **Pigeon**.

It lets two existing AI coding conversations exchange messages through a shared topic. If one is busy, the message waits. Some tools can even wake the same conversation when a message arrives. The beta works with Claude Code, Codex, Antigravity, OpenCode 2 and GitHub Copilot. The two sessions can run on one computer or on two computers on a trusted private network after an explicit join and approval.

Pigeon is free, open source and now in its first public beta. I've tested agent workflows on Windows, but I'd love help finding out what happens on Mac and Linux. If you use any of these tools, would you try it with two new chats and made-up messages? A failed install, a message that only appears after you ask the agent to check, or a chat that doesn't reconnect would all help me improve it.

Download: https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1
Test guide: https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md
Report a bug or request a feature: https://github.com/AndreVianna/Pigeon/issues/new/choose

## Bluesky or Mastodon: short post

Pigeon 0.7.0-beta.1 lets two AI coding sessions exchange durable messages, on one computer or two on a trusted private network. Where supported, it wakes the existing conversation. Integrations for Claude, Codex, Antigravity, OpenCode and Copilot. Mac/Linux testers wanted: https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1

## Factual checklist for an owner-written Show HN

- Link to the repository or release where people can run Pigeon without signing up.
- Explain the personal problem: manually copying messages between Claude Code and Codex.
- Describe two-session topics, a local SQLite queue, MCP/plugins, host-dependent wake, and optional approved peer connection.
- State what has been tested on Windows and in installer CI, and that Mac/Linux agent wake is unverified.
- Mention unsigned installers and unauthenticated local access; point to the beta guide and issue form.
- Be available for questions. Write the submission and all comments yourself under [HN's no-generated-text rule](https://news.ycombinator.com/newsguidelines.html).
