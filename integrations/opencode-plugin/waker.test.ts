// Run with: node --test integrations/opencode-plugin/waker.test.ts (Node 23.6 or later).
import assert from "node:assert/strict"
import { test } from "node:test"
import { mode, turnState, Waker, wakePrompt } from "./pigeon/waker.ts"

const session = "ses_3f0d8a516c2e4d8b"
const generation = "1284d10c6f3f471fa834a87ba13e71fa"

function fakeBroker(options: { claimable?: boolean; tool?: string } = {}) {
  const calls: { operation: string; args: Record<string, any> }[] = []
  const broker = async (operation: string, args: Record<string, any>) => {
    calls.push({ operation, args })
    if (operation === "wake_states")
      return { Ok: true, Value: [{ Tool: "codex", SessionId: "codex-session" }, { Tool: options.tool ?? "opencode", SessionId: session }] }
    if (operation === "wake_claim") return { Ok: true, Value: options.claimable ? { Generation: generation, Attempt: 1 } : null }
    return { Ok: true, Value: {} }
  }
  return { calls, broker }
}

test("sends the fixed wake prompt to a claimed OpenCode session only", async () => {
  const { calls, broker } = fakeBroker({ claimable: true })
  const prompts: [string, string][] = []
  const waker = new Waker(broker, async (id, text) => void prompts.push([id, text]))
  assert.equal(await waker.tick(), 1)
  assert.deepEqual(prompts, [[session, wakePrompt(generation)]])
  assert.ok(prompts[0][1].startsWith(`Pigeon wake generation ${generation}. Call pigeon_receive`))
  assert.deepEqual(calls.filter((c) => c.operation === "wake_claim").map((c) => c.args), [{ sessionId: session, mode }])
  assert.equal(calls.find((c) => c.operation === "wake_result")?.args.outcome, "transport-accepted")
})

test("a session not seen since start is reported idle before its wake is claimed", async () => {
  const { calls, broker } = fakeBroker()
  await new Waker(broker, async () => {}).tick()
  const operations = calls.map((c) => c.operation)
  assert.deepEqual(operations, ["wake_states", "turn_state", "wake_claim"])
  assert.deepEqual(calls[1].args, { tool: "opencode", sessionId: session, state: "idle" })
})

test("a failed prompt is recorded as a transport failure", async () => {
  const { calls, broker } = fakeBroker({ claimable: true })
  await new Waker(broker, async () => { throw new Error("session not found") }).tick()
  assert.equal(calls.find((c) => c.operation === "wake_result")?.args.outcome, "transport-failed")
})

test("nothing is sent when the service refuses the claim", async () => {
  const { broker } = fakeBroker({ claimable: false })
  let prompted = 0
  assert.equal(await new Waker(broker, async () => void prompted++).tick(), 0)
  assert.equal(prompted, 0)
})

test("execution events and session.status map to busy and idle; other events are ignored", () => {
  const at = (type: string, data: any) => turnState({ type, data })
  assert.deepEqual(at("session.execution.started", { sessionID: "a" }), { sessionID: "a", state: "busy" })
  for (const end of ["succeeded", "failed", "interrupted"])
    assert.deepEqual(at(`session.execution.${end}`, { sessionID: "a" }), { sessionID: "a", state: "idle" })
  assert.deepEqual(at("session.status", { sessionID: "a", status: { type: "retry", attempt: 1 } }), { sessionID: "a", state: "busy" })
  assert.deepEqual(at("session.status", { sessionID: "a", status: { type: "idle" } }), { sessionID: "a", state: "idle" })
  assert.equal(at("session.step.started", { sessionID: "a" }), undefined)
  assert.equal(at("session.execution.started", {}), undefined)
})

test("the heartbeat repeats every known state", async () => {
  const { calls, broker } = fakeBroker()
  const waker = new Waker(broker, async () => {})
  await waker.status("a", "busy")
  await waker.status("b", "busy")
  await waker.status("c", "idle")
  calls.length = 0
  await waker.heartbeat()
  assert.equal(calls[0].operation, "bridge_heartbeat")
  assert.equal(calls[0].args.mode, mode)
  assert.match(calls[0].args.id, /^[0-9a-f]{32}$/)
  assert.deepEqual(calls.slice(1).map((c) => [c.args.sessionId, c.args.state]), [["a", "busy"], ["b", "busy"], ["c", "idle"]])
})

test("a failed status report does not throw", async () => {
  const waker = new Waker(async () => { throw new Error("service down") }, async () => {})
  await waker.status("a", "idle")
})
