@mcp
Feature: Agent tools stopped by a signal
  On Linux and macOS, Claude Code ends its MCP server with SIGINT and then SIGTERM. The server still
  tells the service its session's server closed. Windows has no such signals, so this is skipped there.

  Background:
    Given a running service with two agent connections

  Scenario: A server ended with SIGINT and SIGTERM reports its session's close
    Then a pigeon mcp process for a joined Claude session that gets SIGINT and then SIGTERM exits, and the service reports the session's server closed
