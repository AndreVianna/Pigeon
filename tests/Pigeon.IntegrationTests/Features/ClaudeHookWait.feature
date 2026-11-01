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

  Scenario: A newer waiter replaces an older one
    Given an isolated service with a connected Claude session
    Then a second waiter replaces the first, which leaves quietly

  Scenario: An expired waiter leaves quietly and status shows the gap
    Given an isolated service with a connected Claude session
    Then a waiter with a short timeout exits 0 without output and status reports that it expired

  Scenario: An idle waiter renews without claiming a message and its replacement delivers
    Given an isolated service with a connected Claude session
    Then a short renewable waiter requests one renewal and its replacement wakes for a message

  Scenario: A session in no topic does not renew
    Given an isolated service with a connected Claude session
    Then a session in no topic and with nothing waiting expires without a renewal

  Scenario: Session bridge lookup finds only the session's own MCP bridge
    Given an isolated service with a connected Claude session
    Then the bridge lookup finds only the session's own ordinary MCP bridge

  Scenario: Status reports a lost notifier without waiting for another claimant
    Given an isolated service with a connected Claude session
    Then a notifier that stops renewing is reported lost after its ownership lapses

  Scenario: Malformed input or arguments never produce a wake
    Given an isolated service with a connected Claude session
    Then malformed hook input exits 0 and invalid waiter arguments exit 1, without a wake

  Scenario: A waiter survives a service restart
    Given an isolated service with a connected Claude session
    Then a waiter started before a service restart wakes for a message published after it

  Scenario: A waiter needs ownership to claim a wake
    Given an isolated service with a connected Claude session
    Then only the owning waiter can claim, and only its holder can release ownership

  Scenario: A waiter keeps waiting while storage is temporarily locked
    Given an isolated service with a connected Claude session
    Then a waiter started during a held database lock keeps waiting and wakes after the lock is released

  Scenario: A waiter whose outer hook shell is killed stops instead of claiming wakes
    Given an isolated service with a connected Claude session
    Then a waiter under two Git Bash shells exits as orphaned when only the outer shell is killed, and a later message is not claimed

  Scenario: An owning waiter still renews when the service is down at its deadline
    Given an isolated service with a connected Claude session
    Then a short renewable waiter whose service stops before the deadline requests one renewal, and its replacement waits for the service

  Scenario: Status reports versions and a session that never had a notifier
    Given an isolated service with a connected Claude session
    Then status names the service and CLI versions and flags that no notifier was seen for the session
