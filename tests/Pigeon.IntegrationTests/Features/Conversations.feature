@conversations
Feature: Two-member topics
  A topic is a private conversation between two sessions. Messages queue in order for the other member.

  Background:
    Given a fresh message store

  Scenario: The first join creates a topic, the second completes it and a third is refused
    Then the first session to join waits for a partner, the second completes the topic and a third is refused with both members named
    And joining again as a member changes nothing

  Scenario: A session keeps the tool it joined with
    Then a session that joined as codex cannot join another topic as claude-code

  Scenario: A message sent before the partner joins waits for it
    Then a message sent to an empty seat is delivered to whoever joins second

  Scenario: Messages arrive in order and receiving removes them
    Then three messages are received oldest first, and receiving again returns nothing

  Scenario: A replacement receives what the leaver did not read
    Then a member that leaves with unread messages passes them to the session that takes its place

  Scenario: The last member to leave deletes the topic and its messages
    Then the topic and its unread messages are gone after both members leave

  Scenario: Only members can send or receive
    Then a session outside a topic can neither send to it nor receive from it

  Scenario: A retry with the same key does not send twice
    Then resending with the same retry key returns the first message, and different text is refused

  Scenario: Unread messages expire
    Then an unread message is not delivered after its expiry

  Scenario: An abandoned topic is deleted and a recently seen member keeps it
    Then a topic whose members were not seen for the idle period is deleted, and a member seen recently keeps its topic

  Scenario: Retention removes the text of read messages but keeps the retry key
    Then after the retention period a read message has no text and its retry key still prevents a duplicate

  Scenario: A held database lock is reported as storage unavailable
    Then a write during another process's lock fails as storage unavailable and succeeds after it is released
