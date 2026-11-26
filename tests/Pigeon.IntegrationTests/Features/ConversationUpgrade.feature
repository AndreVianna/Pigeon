@conversations
Feature: Upgrading an earlier message store
  An earlier database is upgraded in place the first time the message store opens it.

  Background:
    Given an earlier database with a three-member topic and pending messages

  Scenario: Upgrading an earlier database keeps two members per topic and their pending messages
    When the message store opens it
    Then the two most recently active members remain, their pending message is kept, and the old tables are untouched
