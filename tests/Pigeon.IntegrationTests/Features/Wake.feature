@wake
Feature: Wakes
  A wake tells a session that messages are waiting. Receiving under the wake confirms it, and
  completing it acknowledges what was received.

  Scenario: A burst becomes one wake and a stale generation is refused
    Given a conversation between a Codex and a Claude session
    When the Codex session sends three messages
    Then one wake is claimed for the Claude session, a wrong generation receives nothing, and the right one receives all three in order

  Scenario: Completing a wake acknowledges what it received
    Given a conversation between a Codex and a Claude session
    When the Codex session sends three messages
    Then after receiving and completing the wake nothing is waiting

  Scenario: A message arriving during a wake causes a new wake after completion
    Given a conversation between a Codex and a Claude session
    Then a message sent while a wake is being handled is left for a new wake with a new generation

  Scenario: A wake that never completes returns its messages and is retried
    Given a conversation between a Codex and a Claude session
    Then messages received under an abandoned wake return to the queue and a new wake delivers them again

  Scenario: Wake attempts survive a restart
    Given a conversation between a Codex and a Claude session
    Then a claimed wake keeps its attempt count and retry time after the store is reopened

  Scenario: Unanswered wakes give up, and a new message or a returning session re-arms them
    Given a conversation between a Codex and a Claude session
    Then repeated unanswered wakes stop, a new message re-arms them, and so does restoring the session

  Scenario: The wake mode must match the session's tool
    Given a conversation between a Codex and a Claude session
    Then a Codex queue wake cannot be claimed for the Claude session, nor any wake for an unknown session

  Scenario: A late transport result cannot undo a received wake
    Given a conversation between a Codex and a Claude session
    Then a failed transport result recorded after the wake was received leaves it confirmed

  Scenario: An unreported wake attempt is shown as unknown
    Given a conversation between a Codex and a Claude session
    Then a claimed wake with no transport result is reported as failed with an unknown transport after its retry time

  Scenario: One wake covers all of a session's topics
    Given a conversation between a Codex and a Claude session
    Then a Claude session in two topics receives both messages under one wake, each naming its topic
