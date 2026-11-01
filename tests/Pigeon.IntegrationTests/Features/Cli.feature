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
      | -h        |
      | help      |

  Scenario Outline: Command help does not require a service connection
    When I run Pigeon with arguments "<arguments>"
    Then the command succeeds and writes help to standard output
    And the help contains "<usage>"

    Examples:
      | arguments       | usage                                                      |
      | start -h        | Usage: pigeon start [--foreground]                         |
      | stop --help     | Usage: pigeon stop                                         |
      | status --help   | Usage: pigeon status                                       |
      | topics --help   | Usage: pigeon topics                                       |
      | join --help     | Usage: pigeon join <topic> as <session-id> using <tool>    |
      | leave --help    | Usage: pigeon leave <topic> as <session-id>                |
      | send --help     | Usage: pigeon send from <session-id> to <topic>            |
      | receive --help  | Usage: pigeon receive for <session-id> from <topic>        |
      | help receive    | Usage: pigeon receive for <session-id> from <topic>        |
      | mcp --help      | Usage: pigeon mcp [--codex-queue]                          |
      | hook --help     | Usage: pigeon hook session-start <codex\|claude-code>      |

  Scenario: The version is printed without starting a service
    When I run Pigeon with arguments "--version"
    Then the command succeeds and prints the package version

  Scenario Outline: Invalid usage is reported on standard error
    When I run Pigeon with arguments "<arguments>"
    Then the command reports a usage error on standard error

    Examples:
      | arguments                                     |
      | missing-command                               |
      | --unknown                                     |
      | messages send a b c                           |
      | sessions candidates codex x                   |
      | topics create design                          |
      | topics join design codex x                    |
      | start --invalid                               |
      | status 49372                                  |
      | mcp --claude-channel                          |
      | join design codex x                           |
      | join design as x                              |
      | join design as x with codex                   |
      | leave design x                                |
      | send x to design hello                        |
      | send from x design hello                      |
      | send from x to design                         |
      | receive for x                                 |
      | receive x from design                         |

  Scenario: A conversation through the command line survives a service restart
    Given an isolated background service
    When two sessions join a topic and one sends a message
    And the service is restarted
    Then the topic still has both members and the other session receives the message once
    And the last session to leave deletes the topic

  Scenario: Refusals are reported with exit code 1
    Given an isolated background service
    Then a third join, a send from a non-member and a receive from a non-member each fail with a clear message

  Scenario: A command without a running service says how to start it
    When I run a conversation command without a running service
    Then it fails with exit code 1 and suggests starting the service
