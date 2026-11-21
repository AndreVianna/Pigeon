# Claude directory submission: Pigeon

Status: prepared for owner review; not submitted. The [Claude plugin directory](https://claude.com/resources/articles/build-plugins-for-claude) accepts GitHub-hosted plugin bundles from developers on paid Claude plans and reviews submissions before publication. Pigeon's plugin lives in [`integrations/claude-plugin`](../integrations/claude-plugin) inside this repository.

## Listing draft

**Name:** Pigeon

**Short description:** Let an existing Claude Code session exchange durable messages with a coding agent in another tool.

**Full description:** Pigeon is a free, open-source CLI and local messaging service for two existing AI coding sessions. Its Claude plugin connects through MCP, helps a session join a shared topic, and uses host-supported hooks to notify the same conversation when a message is waiting. Messages remain queued while the partner is busy or disconnected, and the recipient acknowledges them after processing. The other session can be in Codex, Antigravity, OpenCode 2, or GitHub Copilot. Two people can also join their agents across a trusted private network with an explicit join and approval.

**Who it is for:** Developers who keep two coding agents open on the same repository and currently copy questions, review findings, or handoff notes between them.

**Source:** https://github.com/AndreVianna/Pigeon

**Beta download:** https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1

**Setup and test guide:** https://github.com/AndreVianna/Pigeon/blob/v0.7.0-beta.1/docs/beta-testing.md

## Scope to state plainly in the listing

- Install the Pigeon CLI and start its local service before using the plugin. Installing the plugin alone does not install or start Pigeon.
- The plugin's local MCP connection and hooks apply to Claude Code and supported Claude Desktop Code sessions. They are not a cloud connector for ordinary Claude web chat.
- The first beta's agent workflows were exercised on Windows. macOS and Linux packages passed automated checks, but agent wake and recovery there still need user testing.
- Wake behaviour depends on the host and whether that session's plugin and hooks are loaded. A queued message is not proof that a conversation saw or acknowledged it.
- Local callers are unauthenticated in this beta. Use a trusted single-user machine; use the two-machine feature only on a trusted private network.

## Review demonstration to prepare

Use a disposable project and synthetic messages. Show two already-running sessions, one in Claude Code or Claude Desktop Code and one in another supported tool. Join both to a topic, send a message, show the original recipient session receiving it, reply, and show acknowledgment status. Then leave the receiver busy or idle, send a second message, and show what the host actually does. The video must distinguish a live wake from a message found by polling.

## Submission questions to verify in the portal

1. Does the portal accept a GitHub repository whose plugin is in `integrations/claude-plugin`, referenced by the root `.claude-plugin/marketplace.json`, or require a plugin at repository root?
2. Does review accept a local MCP plugin whose `pigeon` executable must be installed separately? If yes, how should that prerequisite appear before installation?
3. Which Claude product surfaces will list and run the plugin's MCP server and hooks? The description must not imply web-chat support for the local service.
4. What screenshots, demo video, support contact, and release URL does the current portal require?

Do not describe Pigeon as an independent hosted MCP service, claim every Claude session wakes live, or publish before the owner reviews the actual portal listing.
