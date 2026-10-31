@session_routes
Feature: Routing by tool and session
  Tool names are shared labels; each session has its own mailbox and subscriptions.

  Scenario: Two sessions of one tool remain separate across reconnection
    Given an isolated topic service with two Claude sessions and a Codex session
    Then direct messages and restored bindings use the tool and session pair

  Scenario: Ambiguous legacy identities are never silently merged
    Given an isolated topic service with two Claude sessions and a Codex session
    Then legacy conflicts are reported without moving queued messages

  Scenario: A closed topic is not recreated when a session restores
    Given an isolated topic service with two Claude sessions and a Codex session
    Then restoring a session never recreates a deleted topic

  Scenario: Startup hook and bridge restore an existing saved session
    Given an isolated topic service with two Claude sessions and a Codex session
    Then the startup hook restores only saved associations and the MCP bridge binds on a later handshake

  Scenario: A legacy session retains its pending message when adopted
    Given an isolated topic service with two Claude sessions and a Codex session
    Then an unambiguous legacy identity keeps its messages and subscriptions when restored

  Scenario: A saved session reconnects after the service returns
    Given an isolated topic service with two Claude sessions and a Codex session
    Then a bridge retries a failed restore when the service becomes available

  Scenario: An opted-in bridge automatically restores its session after an outage
    Given an isolated topic service with two Claude sessions and a Codex session
    Then a new bridge can recover its saved environment session without a prompt

  Scenario: CLI clients address sessions by tool and session ID
    Given an isolated topic service with two Claude sessions and a Codex session
    Then the CLI can join, list and send using tool and session pairs

  Scenario: Simultaneous sessions of one tool cannot overwrite each other
    Given an isolated topic service with two Claude sessions and a Codex session
    Then concurrent tool and session joins create distinct durable routes
