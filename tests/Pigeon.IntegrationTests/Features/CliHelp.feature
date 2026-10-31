@cli_help
Feature: Command line help
  People can discover Pigeon commands without starting a service.

  Scenario Outline: Root help is available through standard entry points
    When I run Pigeon with arguments "<arguments>"
    Then the command succeeds and writes help to standard output
    And the help lists the service commands without a demo

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
      | arguments      | usage                       |
      | start -h       | Usage: pigeon start [--foreground] |
      | mcp --help     | Usage: pigeon mcp [port]    |
      | status --help  | Usage: pigeon status [port] |
      | topics --help  | Usage: pigeon topics        |
      | topics subscribe --help | Usage: pigeon topics subscribe |
      | topics delete --help | Usage: pigeon topics delete |
      | messages --help | Usage: pigeon messages     |
      | messages send --help | Usage: pigeon messages send |
      | messages direct --help | Usage: pigeon messages direct |
      | messages get --help | Usage: pigeon messages get |
      | messages ack --help | Usage: pigeon messages ack |
      | messages status --help | Usage: pigeon messages status |

  Scenario: The version is printed without starting a service
    When I run Pigeon with arguments "--version"
    Then the command succeeds and prints the package version

  Scenario Outline: Invalid usage is reported on standard error
    When I run Pigeon with arguments "<arguments>"
    Then the command reports a usage error on standard error

    Examples:
      | arguments        |
      | missing-command  |
      | --unknown        |
      | demo             |
      | adapters         |
      | serve            |
      | start --invalid  |
      | pause pipe on |
      | register pipe runtime session existing |
      | subscribe pipe topic on |
      | send pipe recipient conversation key 123 |
      | publish pipe topic conversation key 123 |
      | receive pipe |
      | acknowledge pipe receipt |
      | message-status pipe id |
      | message get reader topic |
      | messages post sender topic body |
      | messages send sender topic body --expires-at-unix-ms invalid |
      | messages send sender topic body --reply-to id |
      | messages direct sender recipient body --correlation-id x --correlation-id y |
      | messages direct sender recipient body --bad value |
