@mcp
Feature: Agent tools
  Agents use seven MCP tools. A connection binds to one session the first time it joins.

  Background:
    Given a running service with two agent connections

  Scenario: Agents see only the seven Pigeon tools
    Then the tool list names exactly the seven Pigeon tools

  Scenario: A connection binds to its session and cannot change it
    Then tools other than join ask for a join first, the first join binds the session, and joining as another session is refused

  Scenario: Sending reports whether the recipient is there
    Then a send says nobody else is in the topic, then that the recipient is unavailable, then that it was sent once the recipient is connected

  Scenario: A woken agent receives and completes through its tools
    Then the Claude connection receives the message under its wake and completing the wake leaves nothing waiting

  Scenario: A Claude connection restores its joined session when it starts
    Then a new connection configured for claude-code with a joined session ID is bound without calling join

  Scenario: An Antigravity connection acts for the conversation named in each call
    Then an Antigravity connection joins without a session ID, acts for each calling conversation, restores a joined one, and refuses a call that names none

  Scenario: An Antigravity server reports every conversation it serves as connected
    Then one Antigravity server process shows both of its conversations as connected, and a message to either is delivered as sent

  Scenario: Hosts that cannot show prompts get them in Pigeon's own windows
    Then a Claude desktop opener enters the code in a window, a wrong code is asked again, and the Antigravity joiner's code window stays until the join completes

  Scenario: The local endpoint rejects browser and rebinding requests
    Then the HTTP endpoint rejects browser origins and non-loopback hostnames
