@claude_hook
Feature: Claude hook waiter renewal
  A waiter that reaches its deadline asks for one renewal turn, but only while its session is in a topic.
  These scenarios wait out the shortest timeout, so they run apart from the other waiter scenarios.

  Scenario: An idle waiter renews without claiming a message and its replacement delivers
    Given an isolated service with a connected Claude session
    Then a short renewable waiter requests one renewal and its replacement wakes for a message

  Scenario: A session in no topic expires quietly instead of renewing, and status shows the gap
    Given an isolated service with a connected Claude session
    Then a session in no topic and with nothing waiting expires without a renewal or output, and status reports that it expired
