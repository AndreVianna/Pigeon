@peers
Feature: Agents on other machines
  A session opens a topic to other machines; a session on another machine asks to join it, and the
  people at the two machines pass a join code between them the first time. Two services on loopback
  stand in for two machines, driven through the command line, which is also the fallback when a host
  cannot ask its person.

  Scenario: A first join with a code, then messages both ways, then a known machine without a code
    Given two isolated services, each listening for other machines
    When Ana opens a topic to other machines and Bruno asks to join it by address
    And Bruno confirms the contact, reads his join code, and Ana allows the request with it
    Then a message from each side reaches the other, from the machine and never a session ID
    And each side lists the other as a known machine with nothing left to send
    And a second topic from the known machine is allowed without a code
    And when Ana removes Bruno's machine, Bruno's side ends the topics and forgets Ana's machine too

  Scenario: Nothing is reachable without a person's answer
    Given two isolated services, each listening for other machines
    Then a wrong code is refused, a denied request joins nothing, and Antigravity may ask too

  Scenario: The commands validate their arguments
    Then invalid peer, request and join commands exit with a usage error
