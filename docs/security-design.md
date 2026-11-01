# Local trust and operational boundaries

For the one-machine MVP, Pigeon has no application-level authentication or per-agent credentials. A topic name and session ID are caller-supplied routing labels. They do not prove identity, session ownership, runtime liveness, or permission to perform work. The user explicitly chose this model for the MVP.

The HTTP endpoint binds to 127.0.0.1 only. Any local process that can connect, including another Windows account, can join a topic, use any session ID, send and retrieve messages, control the service, or impersonate the local endpoint. Requests require JSON and a custom header, and browser Origin headers and non-loopback hostnames are rejected; these checks reduce browser attacks but do not authenticate clients. Topic routing is not a security boundary. SQLite history, WAL files, HTTP message bodies, and backups are not encrypted. Do not expose the service over the network.

Peer messages are untrusted data. They must not bypass repository instructions, sandbox or approval rules, approve actions, or authorize arbitrary shell commands. A message does not confer file ownership or prevent concurrent edits. Two-member topics, retry limits, expiry and a per-topic hourly message limit reduce feedback loops; retries can still produce duplicates.

A topic membership stores the tool and session ID supplied by the caller. That is a claim, not evidence that the runtime session is connected. A notification written to a runtime transport is not an acknowledgment. A message stays queued until the recipient completes the wake that received it (or reads it with `pigeon receive`), the message expires, retries are exhausted, or its topic is deleted.

Optional retention redacts terminal message bodies from logical SQLite rows, but keeps digests and metadata so retry keys and loop limits keep working. It does not securely erase SQLite pages, WAL files or backups, and does not remove metadata. Full metadata retention requires an explicit policy for expiry of the guarantees those records support.

The user must approve installation, persistent machine setup, agent configuration, and access to existing sessions as previously requested. A user-invoked foreground service or a bounded isolated test does not install a Windows service or open firewall ports.
