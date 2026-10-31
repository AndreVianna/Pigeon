@messaging
Feature: Shared project messaging
  Agents communicate through one shared service using explicitly acknowledged, durable deliveries.

  Background:
    Given a shared service with registered Codex and Claude identities

  Scenario: A session cannot silently replace another binding
    When Codex registers a different session for its existing identity
    Then the service refuses the request
    And the original Codex binding remains an unverified session claim

  Scenario: A session can re-register the same binding after restart
    When the shared service restarts
    And Codex registers its original session again
    Then the original Codex binding remains an unverified session claim

  Scenario: The session origin is an explicit, unverified claim
    When Codex records its current session as existing
    Then its session origin is reported as existing and unverified
    When Codex claims the same session is newly launched
    Then the service refuses the request

  Scenario: An opted-in service applies body retention on project activity
    Given the shared service has an enabled short body retention policy
    When Codex sends "retention ping" to Claude
    And Claude polls without acknowledging
    And Claude acknowledges the delivery
    And the body retention window elapses
    And Codex inspects its identity
    Then the acknowledged message body is redacted by the service

  Scenario: An offline recipient polls a message after the service restarts
    When Codex sends "ping" to Claude
    And the shared service restarts
    Then Claude can poll "ping"
    When Claude acknowledges the delivery
    Then Codex sees the delivery as "acknowledged"
    And repeating the acknowledgment succeeds
    And the project activity records the acknowledgment

  Scenario: A correlated reply references the received message
    When Codex sends "ping" to Claude
    And Claude polls without acknowledging
    And Claude replies "pong" to the received message
    Then Codex polls "pong" as a reply to the original message

  Scenario: A reply cannot use a different conversation identifier
    When Codex sends "ping" to Claude
    And Claude polls without acknowledging
    And Claude replies using a different conversation identifier
    Then the service refuses the request

  Scenario: Retried sends do not create another message
    When Codex sends "ping" to Claude twice with the same idempotency key
    Then both sends identify the same message
    And Claude can poll "ping"
    When Claude acknowledges the delivery
    Then Claude has no other message waiting

  Scenario: An idempotency key cannot be reused for different content
    When Codex sends "first" to Claude
    And Codex reuses the key with different content
    Then the service refuses the request

  Scenario: A message expires while its recipient is offline
    When Codex sends "ping" to Claude
    And time advances beyond the message expiry
    Then Claude has no other message waiting
    And Codex sees the delivery as "expired"
    And project activity records the "expired" delivery

  Scenario: An unacknowledged delivery is retried with a new receipt
    When Codex sends "ping" to Claude
    And Claude polls without acknowledging
    And time advances beyond the delivery lease
    Then Claude can poll "ping" again
    And the previous receipt is rejected
    When Claude acknowledges the delivery
    Then Codex sees the delivery as "acknowledged"
    And the previous receipt is still rejected
    And Codex sees one timeout and one acknowledgment in the attempt history

  Scenario: A delivery fails after its retry limit
    When Codex sends "ping" to Claude
    And Claude fails to acknowledge three delivery attempts
    Then Codex sees the delivery as "failed"
    And Claude has no other message waiting
    And project activity records the "failed" delivery

  Scenario: A leased message expires before acknowledgment
    When Codex sends "ping" to Claude
    And Claude polls without acknowledging
    And time advances beyond the message expiry
    Then the current receipt is rejected
    And Codex sees the delivery as "expired"

  Scenario: Attempt history records expiry before a later lease timeout
    When Codex sends a message that expires before its lease
    And Claude polls without acknowledging
    And time advances beyond both expiry and lease
    Then Codex sees the delivery as "expired"
    And the attempt history records "expired"

  Scenario: Another project cannot see a message
    When Codex sends "ping" to Claude
    Then the Claude identity in another project cannot inspect the message
    And the Claude identity in another project has an empty inbox

  Scenario: Another project cannot acknowledge a delivery
    When Codex sends "ping" to Claude
    And Claude polls without acknowledging
    Then the Claude identity in another project cannot acknowledge the delivery

  Scenario: Identity labels are not authentication
    When Codex sends "ping" to Claude
    And another local client uses the Claude project and identity labels
    Then that client can poll the message without a credential

  Scenario: A mailbox publication reaches its explicit subscriber
    Given Claude subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox
    Then Claude can poll "design note" from the "design" mailbox

  Scenario: Topic creation is project scoped
    When Codex creates the "design" topic
    Then the topic is visible in Codex's project but not another project

  Scenario: Closing a topic notifies all subscribers and preserves queued deliveries
    Given Claude subscribes to the "design" mailbox
    And a second Claude session subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox
    And Codex deletes the "design" topic
    And the shared service restarts
    Then both subscribers are unsubscribed and receive a closure notice and the queued post

  Scenario: Subscribing to a nonexistent topic fails
    When Claude subscribes to a nonexistent topic
    Then the service refuses the request

  Scenario: Mailbox subscriptions belong to an individual identity
    Given Claude subscribes to the "design" mailbox
    Then Claude sees its "design" subscription and Codex does not

  Scenario: A retried mailbox publication is idempotent
    Given Claude subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox twice with the same key
    Then both sends identify the same message

  Scenario: An unsubscribed reader can pull a post from a topic without subscribers
    When Codex publishes to a mailbox without subscribers
    And the shared service restarts
    Then Claude can pull the post without subscribing

  Scenario: A retry key requires fixed message metadata
    When Codex sends a direct message with only a retry key
    Then the service refuses the request

  Scenario: Topic history can be paged without subscribing and is project scoped
    When Codex publishes four posts to a new topic
    Then Claude reads the posts in sequence without subscribing
    And another project cannot read those posts

  Scenario: Duplicate topic creation warns without replacing the topic
    When Codex creates the "design" topic
    Then creating it again returns an existing topic warning

  Scenario: A later mailbox subscriber does not receive older publications
    Given Claude subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox
    And a second Claude session subscribes to the "design" mailbox
    Then the second Claude session has no older "design" message

  Scenario: Mailbox subscribers acknowledge their own deliveries
    Given Claude subscribes to the "design" mailbox
    And a second Claude session subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox
    And both Claude sessions poll the "design" mailbox
    And Claude acknowledges the delivery
    Then the second Claude delivery is still unacknowledged

  Scenario: Unsubscribing preserves an already queued mailbox delivery
    Given Claude subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox
    And Claude unsubscribes from the "design" mailbox
    Then Claude can poll "design note" from the "design" mailbox

  Scenario: Mailboxes with the same name do not cross projects
    Given Claude subscribes to the "design" mailbox
    And the Claude identity in another project subscribes to the "design" mailbox
    When Codex publishes "design note" to the "design" mailbox
    Then the Claude identity in another project has an empty inbox

  Scenario: MCP reports an unavailable shared service
    When the shared service stops
    Then the MCP connection reports that the shared service is unavailable

  Scenario: Automated exchanges are bounded
    When Codex sends five messages in one automated exchange
    Then the fifth message is rejected

  Scenario: Automated mailbox delivery obeys the exchange limit
    Given Claude subscribes to the "design" mailbox
    When Codex sends four messages in one automated exchange
    And Codex publishes to the "design" mailbox in that exchange
    Then the service refuses the request

  Scenario: Changing correlation IDs does not bypass the project rate limit
    Given the shared service has a project rate limit of two automated deliveries per minute
    When Codex sends three automated messages in different exchanges
    Then the service refuses the request
    When one minute elapses
    Then Codex can send another automated message

  Scenario: A paused project rejects sends and resumes on request
    When Codex pauses the project
    Then Codex cannot send a new message
    When Codex resumes the project
    Then Codex can send a new message

  Scenario: A paused project holds a queued delivery
    When Codex sends "ping" to Claude
    And Codex pauses the project
    Then Claude has no other message waiting
    When Codex resumes the project
    Then Claude can poll "ping"

  Scenario: Inbox status shows queued deliveries and the pause state
    When Codex sends "ping" to Claude
    And Codex pauses the project
    Then Claude sees one queued delivery and a paused project in inbox status
    And Codex sees no queued deliveries in inbox status

  Scenario: Activity history can be read in bounded pages
    When Claude subscribes to twelve different mailboxes
    Then project activity returns distinct bounded pages of history

  Scenario: Activity cursors do not expose another project's event count
    When another project records two subscription events
    And Claude subscribes to the "design" mailbox
    Then this project's activity cursors are contiguous

  Scenario: Concurrent polling does not lease one message twice
    When Codex sends "ping" to Claude
    And Claude polls concurrently from two connections
    Then only one poll receives the message

  Scenario: Separate MCP processes share durable messages across a host restart
    When two MCP processes connect to a separate shared service
    And Codex sends "process ping" through its MCP process
    And the separate shared service restarts
    Then Claude polls "process ping" through its MCP process
    And Claude acknowledges through its MCP process

  Scenario: A second host cannot silently take over the same pipe
    When two MCP processes connect to a separate shared service
    And another service attempts to use the same pipe
    Then the second service refuses to start
    When Codex sends "still shared" through its MCP process
    Then Claude polls "still shared" through its MCP process

  Scenario: The CLI reports the shared service without exposing message bodies
    When two MCP processes connect to a separate shared service
    And Codex sends "activity ping" through its MCP process
    Then the status command reports the service without message bodies

  Scenario: Status tracks connected MCP bridges without claiming agent liveness
    When two MCP processes connect to a separate shared service
    Then status shows both active MCP bridges
    When the separate shared service restarts
    Then status shows both active MCP bridges
    When the Claude MCP process disconnects
    Then status shows only the remaining MCP bridge

  Scenario: The CLI reports the shared service without an agent argument
    When two MCP processes connect to a separate shared service
    Then the status command reports the running service

  Scenario: CLI clients send, receive and acknowledge through the shared service
    When two MCP processes connect to a separate shared service
    And Codex sends "CLI ping" to Claude from the CLI
    Then Claude receives "CLI ping" from the CLI
    When Claude acknowledges the CLI delivery
    Then Codex sees the CLI delivery as acknowledged

  Scenario: A CLI reply refers to the received message
    When two MCP processes connect to a separate shared service
    And Codex sends "CLI ping" to Claude from the CLI
    Then Claude receives "CLI ping" from the CLI
    When Claude replies "CLI pong" from the CLI
    Then Codex receives the correlated CLI reply "CLI pong"

  Scenario: Retrying a direct message with fixed options does not create another delivery
    When two MCP processes connect to a separate shared service
    And Codex retries the same direct CLI message
    Then Claude gets exactly one matching direct CLI delivery

  Scenario: CLI clients use a subscribed mailbox
    When two MCP processes connect to a separate shared service
    And Claude subscribes to "design" from the CLI
    And Codex publishes "CLI topic ping" to "design" from the CLI
    Then Claude receives "CLI topic ping" from the "design" mailbox using the CLI

  Scenario: Topic subscription registers a session and routes messages by name
    When two MCP processes connect to a separate shared service
    And the CLI creates the "project_pigeon" topic
    And the CLI subscribes "bob" using "claude_code" session "session-bob" to "project_pigeon"
    And the CLI subscribes "ana" using "codex" session "session-ana" to "project_pigeon"
    And "ana" sends "Hello, I am Ana." to "project_pigeon" using the grouped CLI
    Then "bob" gets "Hello, I am Ana." from "project_pigeon" using the grouped CLI
    And that delivery is not acknowledged until Bob acknowledges its receipt

  Scenario: A configured Claude channel writes a wake without acknowledging the delivery
    When two MCP processes connect to a separate shared service
    And a test Claude channel connects for its registered session
    And Codex sends "synthetic wake" to the channel identity
    Then the channel transport receives the matching wake
    And the delivery remains queued until the channel client polls and acknowledges

  Scenario: The CLI host applies its configured automated exchange limit
    When two MCP processes connect to a service with an automated exchange limit of one
    And Codex sends "first" through its MCP process
    And Codex sends another message in the same exchange through its MCP process
    Then the service refuses the request
