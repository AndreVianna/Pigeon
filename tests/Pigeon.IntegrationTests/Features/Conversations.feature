@conversations
Feature: Two-member topics
  A topic is a private conversation between two sessions. Messages queue in order for the other member.

  Scenario: The first join creates a topic, the second completes it and a third is refused
    Given a fresh message store
    Then the first session to join waits for a partner, the second completes the topic and a third is refused with both members named
    And joining again as a member changes nothing

  Scenario: A session keeps the tool it joined with
    Given a fresh message store
    Then a session that joined as codex cannot join another topic as claude-code

  Scenario: A message sent before the partner joins waits for it
    Given a fresh message store
    Then a message sent to an empty seat is delivered to whoever joins second

  Scenario: Messages arrive in order and receiving removes them
    Given a fresh message store
    Then three messages are received oldest first, and receiving again returns nothing

  Scenario: A replacement receives what the leaver did not read
    Given a fresh message store
    Then a member that leaves with unread messages passes them to the session that takes its place

  Scenario: The last member to leave deletes the topic and its messages
    Given a fresh message store
    Then the topic and its unread messages are gone after both members leave

  Scenario: Only members can send or receive
    Given a fresh message store
    Then a session outside a topic can neither send to it nor receive from it

  Scenario: A retry with the same key does not send twice
    Given a fresh message store
    Then resending with the same retry key returns the first message, and different text is refused

  Scenario: Unread messages expire
    Given a fresh message store
    Then an unread message is not delivered after its expiry

  Scenario: A topic accepts a bounded number of messages per hour
    Given a fresh message store with a limit of 3 messages per hour
    Then the fourth message within an hour is refused and accepted again an hour later

  Scenario: An abandoned topic is deleted and a recently seen member keeps it
    Given a fresh message store
    Then a topic whose members were not seen for the idle period is deleted, and a member seen recently keeps its topic

  Scenario: Retention removes the text of read messages but keeps the retry key
    Given a fresh message store
    Then after the retention period a read message has no text and its retry key still prevents a duplicate

  Scenario: Upgrading an earlier database keeps two members per topic and their pending messages
    Given an earlier database with a three-member topic and pending messages
    When the message store opens it
    Then the two most recently active members remain, their pending message is kept, and the old tables are untouched

  Scenario: A held database lock is reported as storage unavailable
    Given a fresh message store
    Then a write during another process's lock fails as storage unavailable and succeeds after it is released
