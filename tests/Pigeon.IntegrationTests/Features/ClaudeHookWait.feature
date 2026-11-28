@claude_hook
Feature: Claude hook waiter
  An asyncRewake hook wakes its session only by exiting 2 with the fixed wake instruction on stderr.

  Background:
    Given an isolated service with a connected Claude session

  Scenario: A waiter for an unconnected session leaves at once without output
    Then a waiter for an unknown session exits 0 at once without output

  Scenario: A waiter emits one wake for a burst and exits 2 without leasing messages
    When a waiter is running and two messages arrive
    Then it writes only the fixed wake instruction to stderr, exits 2 and releases its ownership

  Scenario: A newer waiter replaces an older one
    Then a second waiter replaces the first, which leaves quietly

  Scenario: Session bridge lookup finds only the session's own MCP bridge
    Then the bridge lookup finds only the session's own ordinary MCP bridge

  Scenario: Status reports a lost notifier without waiting for another claimant
    Then a notifier that stops renewing is reported lost after its ownership lapses

  Scenario: Malformed input or arguments never produce a wake
    Then malformed hook input exits 0 and invalid waiter arguments exit 1, without a wake

  Scenario: A waiter needs ownership to claim a wake
    Then only the owning waiter can claim, and only its holder can release ownership

  Scenario: A waiter whose outer hook shell is killed stops instead of claiming wakes
    Then a waiter under two Git Bash shells exits as orphaned when only the outer shell is killed, and a later message is not claimed

  Scenario: A waiter leaves once its session's MCP server closed and none replaced it
    Then a waiter leaves without output a few seconds after the session's MCP server reports it closed

  Scenario: A waiter leaves as soon as its session leaves its last topic
    Then a waiter with hours left exits quietly right after its session leaves its only topic

  Scenario: A waiter stays while its session waits for its own join request
    Then a waiter for a session in no topic but asking to join one keeps waiting, and wakes the session with the outcome

  Scenario: Status reports versions and a session that never had a notifier
    Then status names the service and CLI versions and flags that no notifier was seen for the session
