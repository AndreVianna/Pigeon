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

  Scenario: Session bridge lookup finds only the session's own MCP bridge
    Given an isolated service with a connected Claude session
    Then the bridge lookup finds only the session's own ordinary MCP bridge

  Scenario: Status reports a lost notifier without waiting for another claimant
    Given an isolated service with a connected Claude session
    Then a notifier that stops renewing is reported lost after its ownership lapses

  Scenario: Malformed input or arguments never produce a wake
    Given an isolated service with a connected Claude session
    Then malformed hook input exits 0 and invalid waiter arguments exit 1, without a wake

  Scenario: A waiter needs ownership to claim a wake
    Given an isolated service with a connected Claude session
    Then only the owning waiter can claim, and only its holder can release ownership

  Scenario: A waiter whose outer hook shell is killed stops instead of claiming wakes
    Given an isolated service with a connected Claude session
    Then a waiter under two Git Bash shells exits as orphaned when only the outer shell is killed, and a later message is not claimed

  Scenario: Status reports versions and a session that never had a notifier
    Given an isolated service with a connected Claude session
    Then status names the service and CLI versions and flags that no notifier was seen for the session
