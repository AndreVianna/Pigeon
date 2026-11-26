@mcp
Feature: Agent tools without a service
  A connection reports a missing service instead of failing.

  Background:
    Given a connection to a service that is not running

  Scenario: An unavailable service is reported without failing the connection
    Then a tool call reports that the Pigeon service is unavailable
