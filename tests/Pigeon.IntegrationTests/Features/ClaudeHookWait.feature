@claude_hook
Feature: Claude hook waiter
  An asyncRewake hook wakes its session only by exiting 2 with the fixed wake instruction on stderr.

  Scenario: A waiter for an unconnected session leaves at once without output
    Given an isolated service with a connected Claude session
    Then a waiter for an unknown session exits 0 at once without output

  Scenario: A waiter emits one wake for a burst and exits 2 without leasing messages
    Given an isolated service with a connected Claude session
    When a waiter is running and two messages arrive
    Then it writes only the fixed wake instruction to stderr, exits 2 and releases its ownership

  Scenario: A newer waiter replaces an older one and another notifier is not displaced
    Given an isolated service with a connected Claude session
    Then a second waiter replaces the first, and a waiter waits behind a live monitor owner until it stops

  Scenario: An expired waiter leaves quietly and status shows the gap
    Given an isolated service with a connected Claude session
    Then a waiter with a short timeout exits 0 without output and status reports that it expired

  Scenario: An idle waiter renews without claiming a message and its replacement delivers
    Given an isolated service with a connected Claude session
    Then a short renewable waiter requests one renewal and its replacement wakes for a message

  Scenario: A waiter behind a different notifier does not request a renewal
    Given an isolated service with a connected Claude session
    Then a short renewable waiter behind a monitor expires without a renewal

  Scenario: A session with no subscriptions or pending deliveries does not renew
    Given an isolated service with a connected Claude session
    Then an unsubscribed idle session expires without a renewal

  Scenario: Session bridge lookup excludes other sessions and notifier bridges
    Given an isolated service with a connected Claude session
    Then the lightweight bridge lookup returns only distinct matching ordinary MCP identities

  Scenario: Status reports a lost notifier without waiting for another claimant
    Given an isolated service with a connected Claude session
    Then a notifier that stops renewing is reported lost after its ownership lapses

  Scenario: Malformed input or arguments never produce a wake
    Given an isolated service with a connected Claude session
    Then malformed hook input exits 0 and invalid waiter arguments exit 1, without a wake

  Scenario: A waiter survives a service restart
    Given an isolated service with a connected Claude session
    Then a waiter started before a service restart wakes for a message published after it

  Scenario: Claude notifiers need ownership to claim a wake
    Given an isolated service with a connected Claude session
    Then only the owning notifier can claim, and only its holder can release ownership

  Scenario: A waiter keeps waiting while storage is temporarily locked
    Given an isolated service with a connected Claude session
    Then a waiter started during a held database lock keeps waiting and wakes after the lock is released
