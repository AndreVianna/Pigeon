# Architecture

Pigeon has one local service and one SQLite message store. A **topic is a private conversation between at most two sessions**. A session is identified by the session ID its host assigned (a Claude session ID, a Codex thread ID); the tool it belongs to is recorded when it first joins. One session can be in several topics, and one service hosts many topics.

## Topics and messages

The first session to join a topic creates it and waits for a partner; the second completes it; a third is refused with an error naming both members. Rejoining as a member changes nothing. The last member to leave deletes the topic and anything unread in it. A topic whose members have both been disconnected for longer than the idle period (an hour by default, counting only time the service runs) is deleted the same way.

A message goes to the other member of its topic and waits in order until it is read. If the other seat is empty, the message is held and assigned to whoever joins next; a leaving member's unread messages are released to its replacement in the same way. Messages expire after 24 hours. A topic accepts a bounded number of messages per hour, which limits runaway exchanges between agents. Each send carries a retry key, generated automatically, so a retried request is not stored twice.

`send` reports whether the recipient is connected (an MCP bridge for that session sent a heartbeat in the last 15 seconds), only away, or absent from the topic. Connected does not prove that the conversation is active.

## Wakes

Each session with waiting messages has one durable wake generation, shared by a burst. A notifier claims a wake attempt: the Claude plugin's hook waiter writes a fixed instruction and exits so the host starts a turn; the Codex bridge enqueues the same instruction with `codex queue`. The instruction names the generation, never the message. The agent calls `pigeon_receive` with it, which confirms the wake and leases up to 20 messages from all of its topics, each tagged with its topic and sender. `pigeon_wake_complete` acknowledges everything received under the generation. A lease that is not completed in time returns its messages to the queue for a later wake, so delivery is at least once. A stale or delayed instruction cannot receive a later batch. Wake attempts are bounded and retried; a new message or a returning session re-arms a wake that gave up.

Outside a wake, `pigeon receive` on the command line reads and acknowledges in one step.

Only one Claude notifier owns a session's wake claims at a time; ownership is kept in service memory and lapses after 15 seconds without renewal. `pigeon status` shows connected sessions, their topics, wake states and notifier ownership, and every session with waiting messages.

## Boundaries

The service binds only to IPv4 loopback and does not authenticate callers. Any local process that can connect, including processes under another account, can use any session ID, read messages or stop the service. Topics separate routing, not access. Peer messages are data and confer no permission to run commands or bypass repository rules. No cloud, network exposure or autonomous orchestration is in scope. See [local trust boundaries](security-design.md) and [session recovery](session-recovery.md).

Databases from earlier versions are upgraded on first open: sessions, up to two members per topic (the most recently active) and their pending messages are carried over; the earlier tables are left untouched.
