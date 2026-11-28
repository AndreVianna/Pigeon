@peers
Feature: Refusals between machines
  Nothing on another machine is reachable without a person's answer.

  Background:
    Given two isolated services, each listening for other machines

  Scenario: Nothing is reachable without a person's answer
    Then a wrong code is refused and a denied request joins nothing
    And allowing the request after it ended fails, and the asking side shows how it ended
