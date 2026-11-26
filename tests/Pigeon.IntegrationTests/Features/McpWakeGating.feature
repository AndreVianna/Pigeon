@mcp
Feature: Agent tools and wake timing
  OpenCode, Copilot and Antigravity connections claim wakes only after their host reports the session idle, which these scenarios time with a test clock.

  Background:
    Given a running service with a test clock

  Scenario: An Antigravity wake waits until the conversation has been idle
    Then a wake is claimed only after the conversation's hooks report it idle for two seconds, never during a turn, and a lost wake is retried after its retry time

  Scenario: An OpenCode connection acts for the session named in each call, and its plugin's status reports gate wakes
    Then an OpenCode connection joins for the session in the call's metadata, and a wake is claimed only after the plugin reports the session idle for two seconds, also when it repeats that report

  Scenario: A Copilot connection acts for the session in its environment, and its extension's turn reports gate wakes
    Then a Copilot connection joins for the session the runtime named in its environment, falls back to VS Code's chat ID without it, and a wake is claimed only after the extension reports the session idle for two seconds
