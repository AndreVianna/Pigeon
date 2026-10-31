# Local trust and operational boundaries

For the one-machine MVP, Pigeon has no application-level authentication or per-agent credentials. A topic name and agent name are caller-supplied routing labels. They do not prove identity, session ownership, runtime liveness, or permission to perform work. The user explicitly chose this model for the MVP.

The HTTP endpoint binds to 127.0.0.1 only. Any local process that can connect, including another Windows account, can select a topic or impersonate an agent name, send and retrieve messages, control the service, or impersonate the local endpoint. Requests require JSON and a custom header, and browser Origin headers and non-loopback hostnames are rejected; these checks reduce browser attacks but do not authenticate clients. Topic routing is not a security boundary. SQLite history, WAL files, HTTP message bodies, and backups are not encrypted. Do not expose the service over the network.

Peer messages are untrusted data. They must not bypass repository instructions, sandbox or approval rules, approve actions, or authorize arbitrary shell commands. A message does not confer file ownership or prevent concurrent edits. Retry limits, expiry, explicit recipients, rate limits and pause controls reduce feedback loops; retries can still produce duplicates.

A subscription stores the runtime and session ID supplied by the caller. That is a claim, not evidence that the runtime session is connected. A notification written to a runtime transport is not an agent acknowledgment. The inbox remains durable until a recipient explicitly acknowledges, the message expires, or retries are exhausted.

Optional retention redacts terminal message bodies from logical SQLite rows, but keeps digests and metadata for idempotency and loop limits. It does not securely erase SQLite pages, WAL files or backups, and does not remove metadata. Full metadata retention requires an explicit policy for expiry of the guarantees those records support.

The user must approve installation, persistent machine setup, agent configuration, and access to existing sessions as previously requested. A user-invoked foreground service or a bounded isolated test does not install a Windows service or open firewall ports.
