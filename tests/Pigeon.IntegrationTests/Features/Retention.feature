@retention
Feature: Opt-in message body retention
  Retention removes terminal message contents without resetting delivery or loop safety metadata.

  Scenario: Acknowledged content is redacted without affecting queued messages or idempotency
    Given a retention store with registered agents
    When an automated message is acknowledged and another remains queued
    And the retention window elapses
    And terminal bodies are redacted
    Then only the acknowledged content is redacted
    And the original idempotency and automated exchange limit still apply
    And the queued content can still be polled

  Scenario: A publication retains all copies while one subscriber is offline
    Given a retention store with registered agents
    When a publication is acknowledged by one of two subscribers
    And the retention window elapses
    And terminal bodies are redacted
    Then no publication content is redacted
    When the other subscriber acknowledges the publication
    And the retention window elapses
    And terminal bodies are redacted
    Then the publication content is redacted for both subscribers
    And a retry returns the original publication deliveries

  Scenario: A retention window survives reopening the database
    Given a retention store with registered agents
    When an automated message is acknowledged and another remains queued
    And the retention store is reopened
    And the retention window elapses
    And terminal bodies are redacted
    Then only the acknowledged content is redacted

  Scenario: An existing prototype database is upgraded without losing history
    Given an older store with an acknowledged message
    When the older store is opened by the current service
    Then the old message is preserved with a fresh retention window
    When the retention window elapses
    And terminal bodies are redacted
    Then the old message body is redacted and its idempotency key remains valid
