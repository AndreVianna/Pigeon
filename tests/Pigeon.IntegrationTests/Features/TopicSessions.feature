@topic_sessions
Feature: Sessions communicate through project topics
  One service holds multiple topics without a separate project namespace.

  Scenario: Sessions join different topics through the same MCP configuration
    Given four unregistered MCP sessions share one topic-mode service
    When two sessions join each of two project topics
    And one session posts to each topic
    Then only the other subscriber to each topic receives its post
    And an unsubscribed session can pull another topic's post
    And direct messaging between unrelated topics is rejected
    And direct messaging selects the correct shared topic when sessions share two topics

  Scenario: The local HTTP endpoint rejects browser and rebinding requests
    Given four unregistered MCP sessions share one topic-mode service
    Then the HTTP endpoint rejects browser origins and non-loopback hostnames

  Scenario: An opted-in Codex bridge queues one wake without acknowledging a message
    Given four unregistered MCP sessions share one topic-mode service
    When a subscriber receives a topic post through the mocked Codex queue
    Then the queue request targets only that session and leaves the delivery unacknowledged

  Scenario: The operator can re-arm a recipient's pending wake through the CLI
    Given four unregistered MCP sessions share one topic-mode service
    When a pending wake is retried through the CLI
    Then the old generation is stale and the delivery remains queued

  Scenario: A Claude channel coalesces a burst and requires the registered session
    Given four unregistered MCP sessions share one topic-mode service
    Then a mismatched Claude channel cannot claim a wake and the correct channel confirms one burst
