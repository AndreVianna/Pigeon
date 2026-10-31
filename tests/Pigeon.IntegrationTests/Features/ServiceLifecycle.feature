@service_lifecycle
Feature: Local service lifecycle
  The CLI can start and stop one local service without installing a Windows service.

  Scenario: A background service restarts with its project topics intact
    When I start an isolated background service
    Then the service reports that it is running
    When I create a topic in that service
    And I stop the isolated background service
    Then the service reports that it is unavailable
    When I start an isolated background service
    Then the previously created topic is still available
