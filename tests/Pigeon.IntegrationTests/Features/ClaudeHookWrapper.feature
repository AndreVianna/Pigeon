@claude_wrapper
Feature: Claude hook compatibility guard
  The wrapper must only wake Claude for Pigeon's fixed wake or renewal output.

  Scenario: Incompatible CLI failures cannot cause repeated model wakes
    Given the production Claude hook wrapper and an isolated fake CLI
    Then valid wakes and renewals pass through and CLI errors remain silent
