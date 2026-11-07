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

  Scenario: An Antigravity connection acts for the conversation named in each call
    Given a running service with two agent connections
    Then an Antigravity connection joins without a session ID, acts for each calling conversation, restores a joined one, and refuses a call that names none

  Scenario: An Antigravity server reports every conversation it serves as connected
    Given a running service with two agent connections
    Then one Antigravity server process shows both of its conversations as connected, and a message to either is delivered as sent

  Scenario: Hosts that cannot show prompts get them in Pigeon's own windows
    Given a running service with two agent connections
    Then a Claude desktop opener enters the code in a window, a wrong code is asked again, and the Antigravity joiner's code window stays until the join completes

  Scenario: An Antigravity wake waits until the conversation has been idle
    Given a running service with a test clock
    Then a wake is claimed only after the conversation's hooks report it idle for two seconds, never during a turn, and a lost wake is retried after its retry time

  Scenario: An OpenCode connection acts for the session named in each call, and its plugin's status reports gate wakes
    Given a running service with a test clock
    Then an OpenCode connection joins for the session in the call's metadata, and a wake is claimed only after the plugin reports the session idle for two seconds, also when it repeats that report

  Scenario: A Copilot connection acts for the session in its environment, and its extension's turn reports gate wakes
    Given a running service with a test clock
    Then a Copilot connection joins for the session the runtime named in its environment, falls back to VS Code's chat ID without it, and a wake is claimed only after the extension reports the session idle for two seconds

  Scenario: The local endpoint rejects browser and rebinding requests
    Given a running service with two agent connections
    Then the HTTP endpoint rejects browser origins and non-loopback hostnames

  Scenario: An unavailable service is reported without failing the connection
    Given a connection to a service that is not running
    Then a tool call reports that the Pigeon service is unavailable
