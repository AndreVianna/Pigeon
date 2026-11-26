@peer_tls
Feature: Peer TLS boundary
  Ana's peer listener runs over real mutual TLS on loopback, with an identity of its own. A stranger
  is a machine Ana never joined: it gets through the handshake only while a topic is open to other
  machines, and then reaches only the join paths. Each side pins the other's fingerprint.

  Background:
    Given Ana's peer listener on loopback and a machine she does not know

  Scenario: An unknown machine is refused at the handshake while no topic is open
    When the stranger asks to start a join
    Then the handshake fails

  # Found in CI on 2026-11-07: .NET on macOS accepts no TLS 1.3 connections, so a Mac talks TLS 1.2.
  Scenario: A machine limited to TLS 1.2 still completes the mutual handshake
    Given Ana has a topic open to other machines
    When the stranger, limited to TLS 1.2, asks for Ana's topics
    Then the requests are refused after the handshake

  Scenario: A client refuses a server with another identity
    Given Ana has a topic open to other machines
    When the stranger asks to start a join, expecting another identity than Ana's
    Then the handshake fails

  Scenario: While a topic is open an unknown machine reaches only the join paths
    Given Ana has a topic open to other machines
    When the stranger delivers a message and asks for Ana's topics
    Then the requests are refused after the handshake
    When the stranger asks to join Ana's open topic
    Then the request is accepted, and Ana's topic still has only her in it
