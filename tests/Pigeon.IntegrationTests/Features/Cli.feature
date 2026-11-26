@cli
Feature: Command line
  People run Pigeon with short sentence-like commands; help works without a service.

  Scenario Outline: Root help is available through standard entry points
    When I run Pigeon with arguments "<arguments>"
    Then the command succeeds and writes help to standard output
    And the help lists the commands people use and the ones used by the plugins

    Examples:
      | arguments |
      |           |
      | --help    |
      | help      |

  Scenario Outline: Command help does not require a service connection
    When I run Pigeon with arguments "<arguments>"
    Then the command succeeds and writes help to standard output
    And the help contains "<usage>"

    Examples:
      | arguments       | usage                                                      |
      | start -h        | Usage: pigeon start [--foreground]                         |
      | join --help     | Usage: pigeon join <topic> as <session-id> using <tool>    |
      | help receive    | Usage: pigeon receive for <session-id> from <topic>        |
      | hook --help     | Usage: pigeon hook session-start <codex\|claude-code\|antigravity> |

  Scenario: The version is printed without starting a service
    When I run Pigeon with arguments "--version"
    Then the command succeeds and prints the package version

  Scenario Outline: Invalid usage is reported on standard error
    When I run Pigeon with arguments "<arguments>"
    Then the command reports a usage error on standard error

    Examples:
      | arguments                                     |
      | missing-command                               |
      | start --invalid                               |
      | join design as x with codex                   |
      | leave design x                                |
      | send from x to design                         |
      | receive x from design                         |
      | sidecar codex                                 |

  Scenario Outline: Antigravity hooks always answer, even without a service
    When I run Pigeon with arguments "<arguments>"
    Then the command succeeds and prints exactly '<reply>'

    Examples:
      | arguments                          | reply                 |
      | hook turn-start antigravity        | {}                    |
      | hook session-start antigravity     | {}                    |
      | hook turn-end antigravity          | {"decision":"allow"}  |

  Scenario: A turn hook does not wait for a service that is not running
    Then an Antigravity turn hook with a conversation ID answers within a second when no service runs

  Scenario: The Antigravity sidecar refuses to run outside Antigravity
    When I run Pigeon with arguments "sidecar antigravity"
    Then the command fails and says it runs inside the Antigravity App

  Scenario: The Antigravity sidecar stops when the process that started it exits
    Then a sidecar under two Git Bash shells exits when only the outer shell is killed

  Scenario: A conversation through the command line survives a service restart and refuses a third session
    Given an isolated background service
    When two sessions join a topic and one sends a message
    And the service is restarted
    Then the topic still has both members and the other session receives the message once
    And a third join, a send from a non-member and a receive from a non-member each fail with exit code 1 and a clear message
    And the last session to leave deletes the topic

  Scenario: A command without a running service says how to start it
    When I run a conversation command without a running service
    Then it fails with exit code 1 and suggests starting the service
