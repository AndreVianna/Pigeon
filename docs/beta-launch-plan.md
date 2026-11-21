# Pigeon beta invitation: a two-hour launch session

Plan prepared 2026-11-12 for the public [0.7.0-beta.1 release](https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1). The owner reports publishing on LinkedIn, r/ClaudeCode and Facebook and sending the Console.dev pitch; X was skipped because account verification email was unavailable. Publication links have not been independently checked. No directory submission has been reported. The [announcement kit](beta-invitation.md) holds reusable copy; the [channel research](beta-launch-channels.md) records the venues and their rules.

## Goal and working limit

Get useful beta reports from people running AI coding agents, especially on macOS and Linux. A partial installation failure counts. Measure whether testers can install, connect two sessions, exchange a message, wake an existing session, and acknowledge it. Avoid judging this beta by views, votes, or the number of sites carrying the announcement.

The initial outreach can be done in **one two-hour session** because the announcement, LinkedIn post and comment, Reddit challenge, X/Facebook text, and Console.dev pitch are already drafted. Replies and beta reports arrive afterward; handling them is ongoing support, not a reason to spread the initial posts over two weeks.

## One reusable information set

Review these once before publishing; reuse the facts in every venue, changing only the tone and length:

- One sentence: **Pigeon lets two AI coding sessions exchange durable messages on one computer or, with explicit approval, across a trusted private network; it can wake an existing conversation where its host supports it.**
- Download: [v0.7.0-beta.1 release](https://github.com/AndreVianna/Pigeon/releases/tag/v0.7.0-beta.1).
- Instructions: [beta testing guide](beta-testing.md).
- Feedback: [beta report form](https://github.com/AndreVianna/Pigeon/issues/new?template=beta-report.yml) and [feature request issue chooser](https://github.com/AndreVianna/Pigeon/issues/new/choose).
- Limits to state plainly: agent workflows exercised on Windows; Mac/Linux real-agent wake remains unverified; installers are unsigned; local callers are unauthenticated, so use disposable sessions and synthetic messages on a single-user test machine.

A short screen capture of two sessions exchanging a synthetic message would help later directories and Product Hunt. It is **not** a gate for the first tester invitations; the release and guide already let people try Pigeon.

## Two-hour run sheet

The time boxes are a guide, not waiting periods. Move straight to the next action when ready.

| Elapsed time | Action | Check before moving on |
|---|---|---|
| **0–10 min** | Open the release, beta guide, and report form. Skim the drafts in the [announcement kit](beta-invitation.md). | Version, links, and beta limitations still match the release. |
| **10–30 min** | Publish the **LinkedIn product post** with the [infographic](media/pigeon-linkedin-infographic-v5.png), then add the **first comment** requesting testers. | Put no links in the post. Save the post link; the first comment carries the release, guide, and report links. |
| **30–55 min** | Post the test-of-fire challenge in the current [r/ClaudeCode weekly showcase](https://www.reddit.com/r/ClaudeCode/). | Check the current thread and rules. Add one truthful sentence on how Claude Code helped build or test Pigeon; use a comment rather than a standalone post unless the post meets its substance rule. Save the link. |
| **55–70 min** | Send the prepared **Console.dev editorial pitch**. | Review it in the owner's voice before sending; [Console.dev](https://console.dev/selection-criteria) explicitly lists developer-tool betas. Save a copy. |
| **70–95 min** | If account access allows it, publish the short **X thread** and/or the **Facebook personal-network post**. Otherwise, prepare a developer-tool listing such as DevHunt. | Follow the chosen community's promotion rules. Describe Pigeon as a CLI messaging app with agent integrations. |
| **95–120 min** | Record the published links, answer any immediate questions, and open a GitHub issue for any reproducible failure. | Each channel points to the same release, guide, and beta report form. |

The core launch is LinkedIn, r/ClaudeCode, and Console.dev. X, Facebook, and a directory are optional use of the remaining time; missing one does not make the launch incomplete. Technical directory entries and link checks can be prepared separately for owner review. Account-owned posts and replies stay in the owner's voice.

## What waits

- **Bluesky and Mastodon:** share a pointer only if the owner already has a relevant audience there. They need no separate launch campaign.
- **r/ClaudeAI, r/dotnet, r/MacApps:** use only if the first wave reveals interested testers and current community rules allow it. r/dotnet requires owner-written, non-AI post text.
- **DevHunt and AlternativeTo:** tackle one at a time after the first beta reports. DevHunt's permanent listing is free, but its featured launch costs $49. Prepare a logo and a real product screenshot before submitting.
- **Claude/Copilot plugin directories:** these list specific integrations rather than Pigeon as a whole. Their review requirements are in the [research](beta-launch-channels.md).
- **Show HN and Product Hunt:** separate launch moments once an outside user has tested the workflow, a short demo exists, and the owner has time to answer questions that day. HN requires the owner to write all post and comment text personally.
- **Paid listings:** no spending is needed for this first beta invitation. DevHunt's featured launch is paid, and BetaList currently requires payment.

## After the launch session

Check replies and GitHub issues when available. If reports show that installation or notification repeatedly fails on a platform, fix the guide or product and answer those testers before widening the campaign. Once the first reports arrive, choose **one** next audience based on what is missing: a Mac/Linux community for platform coverage, a technical article for developer understanding, or a larger launch site for awareness. There is no fixed two-week wait.
