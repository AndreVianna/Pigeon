@claude_monitor
Feature: Claude session monitor
  A monitor wakes only its bound session without leasing the delivery.

  Scenario: A monitor waits for its session and emits one durable wake for a burst
    Given an isolated service and a Claude monitor for a registered session
    When messages arrive while that session's bridge is unavailable
    Then the monitor waits until the matching bridge connects and emits a single wake without leasing messages

  Scenario: A monitor exits when its launcher ends
    Given an isolated service and a Claude monitor for a registered session
    Then an orphaned monitor exits without claiming a later message

  Scenario: A monitor exits when its output listener disappears
    Given an isolated service and a Claude monitor for a registered session
    Then a broken output pipe does not cause repeated wake claims
