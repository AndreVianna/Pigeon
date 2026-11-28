@mcp
Feature: Agent tools without a service
  A connection reports a missing service instead of failing.

  Background:
    Given a connection to a service that is not running

  Scenario: A service that is not running is reported with how to start it
    Then a tool call reports that the Pigeon service is not running and how to start it
