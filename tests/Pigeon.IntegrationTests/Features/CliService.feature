@cli
Feature: Command line with a service
  The commands drive a background service that survives a restart and keeps its topics private to their members.

  Background:
    Given an isolated background service

  Scenario: A conversation through the command line survives a service restart and refuses a third session
    When two sessions join a topic and one sends a message
    And the service is restarted
    Then the topic still has both members and the other session receives the message once
    And a third join, a send from a non-member and a receive from a non-member each fail with exit code 1 and a clear message
    And the last session to leave deletes the topic
