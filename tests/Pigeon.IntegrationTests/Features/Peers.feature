@peers
Feature: Peers on other machines
  Two people pair their Pigeon services and share one topic at a time; their agents then talk as
  if they were on one machine. Two services on loopback stand in for two machines.

  Scenario: Two people pair their services and their sessions talk on a shared topic
    Given two isolated services, each named and listening for peers
    When Ana invites Bruno, Bruno accepts and confirms the check code, and Ana confirms the same code
    And Ana shares a topic with Bruno and both sessions join it
    Then a message from each side reaches the other, from the peer and never a session ID
    And each side lists the other as a peer with nothing left to send

  Scenario: Nothing is reachable without pairing and sharing
    Given two isolated services, each named and listening for peers
    Then joining a topic that was not shared fails, a wrong check code pairs nothing, and an invitation works once

  Scenario: The peer commands validate their arguments
    Then invalid peer and topic commands exit with a usage error
