@claude_hook
Feature: Claude hook waiter and outages
  A waiter keeps its session reachable while the service restarts, is down, or cannot reach its storage.

  Scenario: A waiter survives a service restart
    Given an isolated service with a connected Claude session
    Then a waiter started before a service restart wakes for a message published after it

  Scenario: A waiter keeps waiting while storage is temporarily locked
    Given an isolated service with a connected Claude session
    Then a waiter started during a held database lock keeps waiting and wakes after the lock is released

  Scenario: An owning waiter still renews when the service is down at its deadline
    Given an isolated service with a connected Claude session
    Then a short renewable waiter whose service stops before the deadline requests one renewal, and its replacement waits for the service
