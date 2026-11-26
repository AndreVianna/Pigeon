@peers
Feature: Refusals between machines
  Nothing on another machine is reachable without a person's answer, and the commands check their arguments.

  Scenario: Nothing is reachable without a person's answer
    Given two isolated services, each listening for other machines
    Then a wrong code is refused and a denied request joins nothing

  Scenario: The commands validate their arguments
    Then invalid peer, request and join commands exit with a usage error
