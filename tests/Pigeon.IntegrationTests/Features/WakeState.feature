@wake_state
Feature: Durable wake delivery
  Wake transport, confirmation and message acknowledgment have separate states.

  Scenario: A burst coalesces into one confirmed wake and stale wakes do nothing
    Given a durable Codex inbox with a pending message
    When a wake is claimed and another message arrives
    Then only one wake is outstanding until the agent confirms and drains the inbox

  Scenario: Wake attempts and backoff survive a service restart
    Given a durable Codex inbox with a pending message
    When a wake transport fails and the service restarts
    Then retries remain bounded and explicit retry starts a new generation

  Scenario: A confirmed wake is retried after an abandoned message lease
    Given a durable Codex inbox with a pending message
    When the agent confirms a wake but abandons a leased message
    Then the next wake has a new generation and the old wake is stale

  Scenario: A late transport result cannot undo agent confirmation
    Given a durable Codex inbox with a pending message
    When the agent confirms a wake before the transport result arrives
    Then the wake remains confirmed and the message remains unacknowledged

  Scenario: Wake claims honor pause, expiry and recipient isolation
    Given a durable Codex inbox with a pending message
    Then paused, expired and wrong-project wake requests cannot consume that inbox

  Scenario: Concurrent bridges cannot claim the same wake attempt
    Given a durable Codex inbox with a pending message
    Then concurrent claimants obtain only one wake attempt

  Scenario: A monitor or hook waiter cannot claim a different or rebound session's wake
    Given a durable Codex inbox with a pending message
    Then a Claude monitor must match both the runtime and current session binding

  Scenario: An interrupted transport attempt is visible after recovery
    Given a durable Codex inbox with a pending message
    When a bridge crashes after claiming a wake
    Then the retry records the earlier transport outcome as unknown

  Scenario: A new message after acknowledgment makes the old wake stale
    Given a durable Codex inbox with a pending message
    When the first wake is confirmed and its message acknowledged
    Then a later message receives a new generation that the old wake cannot confirm

  Scenario: A long-lived recipient can receive more than 1024 wake generations
    Given a durable Codex inbox with a pending message
    Then 1025 successive wake generations can be confirmed and drained

  Scenario: A stored wake is visible before any bridge connects and survives restart
    Given a durable Codex inbox with a pending message
    Then the offline recipient's ready wake remains visible after a service restart

  Scenario: Messages arriving during a confirmed batch cause a new wake after completion
    Given a durable Codex inbox with a pending message
    Then finishing a partial batch re-arms work that arrived while processing

  Scenario: Pending deliveries from the previous schema acquire durable wake state
    Given a durable Codex inbox with a pending message
    When the wake tables are absent as in the previous version
    Then the next service startup preserves the delivery and creates its wake lazily

  Scenario: Repeated confirmations without message progress exhaust the wake budget
    Given a durable Codex inbox with a pending message
    Then repeated confirmation without acknowledgement cannot cause an unbounded wake loop
