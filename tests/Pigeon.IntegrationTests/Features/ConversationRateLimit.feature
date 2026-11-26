@conversations
Feature: Topic message limit
  A topic accepts only a bounded number of messages per hour.

  Background:
    Given a fresh message store with a limit of 3 messages per hour

  Scenario: A topic accepts a bounded number of messages per hour
    Then the fourth message within an hour is refused and accepted again an hour later
