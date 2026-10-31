@topic_migration
Feature: Migrating the previous namespace model
  Existing Pigeon data must not be silently merged or lost.

  Scenario: Distinct legacy topics and queued messages migrate to one topic space
    Given a legacy database with distinct topics and identities
    When the database is opened in topic mode
    Then both topics and the queued delivery are available

  Scenario: Conflicting legacy topic names do not merge automatically
    Given a legacy database with conflicting topic names
    When migration is attempted
    Then migration fails and the legacy topics remain unchanged

  Scenario: Conflicting legacy agent names do not merge automatically
    Given a legacy database with conflicting agent names
    When migration is attempted
    Then migration fails and both legacy agent bindings remain unchanged
