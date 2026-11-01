@mcp
Feature: Agent tools
  Agents use seven MCP tools. A connection binds to one session the first time it joins.

  Scenario: Agents see only the seven Pigeon tools
    Given a running service with two agent connections
    Then the tool list names exactly the seven Pigeon tools

  Scenario: A connection binds to its session and cannot change it
    Given a running service with two agent connections
    Then tools other than join ask for a join first, the first join binds the session, and joining as another session is refused

  Scenario: Sending reports whether the recipient is there
    Given a running service with two agent connections
    Then a send says nobody else is in the topic, then that the recipient is unavailable, then that it was sent once the recipient is connected

  Scenario: A woken agent receives and completes through its tools
    Given a running service with two agent connections
    Then the Claude connection receives the message under its wake and completing the wake leaves nothing waiting

  Scenario: A Claude connection restores its joined session when it starts
    Given a running service with two agent connections
    Then a new connection configured for claude-code with a joined session ID is bound without calling join

  Scenario: The local endpoint rejects browser and rebinding requests
    Given a running service with two agent connections
    Then the HTTP endpoint rejects browser origins and non-loopback hostnames

  Scenario: An unavailable service is reported without failing the connection
    Given a connection to a service that is not running
    Then a tool call reports that the Pigeon service is unavailable
