// Run with: node --test integrations/copilot-plugin/waker.test.mjs
import assert from "node:assert/strict"
import { test } from "node:test"
import { isPigeonTool, mode, turnState, Waker, wakePrompt } from "./extensions/pigeon/waker.mjs"

const session = "09da179d-fe71-48c8-9d66-e52f1838fdce"
const generation = "1284d10c6f3f471fa834a87ba13e71fa"

function fakeBroker(claimable) {
  const calls = []
  const broker = async (operation, args) => {
    calls.push({ operation, args })
    if (operation === "wake_claim") return { Ok: true, Value: claimable ? { Generation: generation, Attempt: 1 } : null }
    return { Ok: true, Value: {} }
  }
  return { calls, broker }
}

test("a granted wake sends the fixed prompt to its own session and records the outcome", async () => {
  const { calls, broker } = fakeBroker(true)
  const sent = []
  const waker = new Waker({ broker, send: async (text) => void sent.push(text), sessionId: session })
  assert.equal(await waker.tick(), true)
  assert.deepEqual(sent, [wakePrompt(generation)])
  assert.ok(sent[0].startsWith(`Pigeon wake generation ${generation}. Call pigeon_receive`))
  assert.deepEqual(calls[0], { operation: "wake_claim", args: { sessionId: session, mode } })
  assert.deepEqual(calls[1].args, { sessionId: session, generation, attempt: 1, outcome: "transport-accepted" })
})

test("a failed send is recorded as a transport failure", async () => {
  const { calls, broker } = fakeBroker(true)
  await new Waker({ broker, send: async () => { throw new Error("closed") }, sessionId: session }).tick()
  assert.equal(calls.find((c) => c.operation === "wake_result").args.outcome, "transport-failed")
})

test("nothing is sent when the service refuses the claim", async () => {
  const { broker } = fakeBroker(false)
  let sent = 0
  assert.equal(await new Waker({ broker, send: async () => void sent++, sessionId: session }).tick(), false)
  assert.equal(sent, 0)
})

test("turns map to busy and idle, and the heartbeat repeats the current state", async () => {
  assert.equal(turnState("assistant.turn_start"), "busy")
  assert.equal(turnState("session.idle"), "idle")
  assert.equal(turnState("assistant.turn_end"), undefined)
  const { calls, broker } = fakeBroker(false)
  const waker = new Waker({ broker, send: async () => {}, sessionId: session })
  await waker.turn("busy")
  calls.length = 0
  await waker.heartbeat()
  assert.equal(calls[0].operation, "bridge_heartbeat")
  assert.equal(calls[0].args.mode, mode)
  assert.equal(calls[0].args.sessionId, session)
  assert.match(calls[0].args.id, /^[0-9a-f]{32}$/)
  assert.deepEqual(calls[1].args, { tool: "copilot", sessionId: session, state: "busy" })
})

test("only Pigeon's own MCP tools are allowed", () => {
  assert.equal(isPigeonTool({ kind: "mcp", serverName: "pigeon", toolName: "pigeon-pigeon_receive" }), true)
  assert.equal(isPigeonTool({ kind: "mcp", serverName: "pigeon-dev", toolName: "pigeon-dev-pigeon_send" }, "pigeon-dev"), true)
  assert.equal(isPigeonTool({ kind: "mcp", serverName: "other", toolName: "other-pigeon_receive" }), false)
  assert.equal(isPigeonTool({ kind: "mcp", serverName: "pigeon", toolName: "pigeon-delete_everything" }), false)
  assert.equal(isPigeonTool({ kind: "shell", fullCommandText: "pigeon send" }), false)
  assert.equal(isPigeonTool(undefined), false)
})

test("a failed state report does not throw", async () => {
  await new Waker({ broker: async () => { throw new Error("down") }, send: async () => {}, sessionId: session }).turn("idle")
})
